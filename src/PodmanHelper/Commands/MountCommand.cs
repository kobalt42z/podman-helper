using System.Diagnostics;
using PodmanHelper.Cli;
using PodmanHelper.Config;
using PodmanHelper.Mutagen;
using PodmanHelper.Vm;

namespace PodmanHelper.Commands;

/// <summary>
/// The command Visual Studio runs before compose up: sync every entry into the machine and bind-mount
/// the copies over their 9p paths. Exit 0 only when everything is in place.
/// </summary>
public static class MountCommand
{
    public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var ws = Workspace.Load(cli.Get("project"), requireProject: true);
        ReportConfig(ws);
        if (ws.Merged.HasErrors)
        {
            return 2;
        }
        if (ws.Entries.Count == 0)
        {
            Output.Ok("nothing to mount (no entries)");
            return 0;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(10, cli.GetInt("timeout", 180)));
        var ctx = await Context.ConnectAsync(cli, startMachine: !cli.Flag("no-start"), ct);
        var entries = ws.Entries;

        foreach (var e in entries)
        {
            if (!Directory.Exists(e.WindowsPath) && !File.Exists(e.WindowsPath))
            {
                throw new HelperException($"{e.SourceFile}:{e.Line}: Windows path does not exist: {e.WindowsPath}" +
                                          " (relative paths resolve from the folder of the filemap file; use a full path or ~\\...)");
            }
        }

        Output.Step("preparing sync directories in the machine");
        await ctx.Ssh.RunScriptOrThrowAsync(VmScripts.Prepare(entries, ctx.Machine.SshUser), privileged: true, "preparing sync directories", ct: ct);

        Output.Step("starting mutagen daemon");
        await ctx.Mutagen.DaemonStartAsync(ct);
        var sessions = (await ctx.Mutagen.ListAsync(ct)).ToDictionary(s => s.Name, StringComparer.Ordinal);

        foreach (var e in entries)
        {
            await EnsureSessionAsync(ctx, e, sessions, ct);
        }

        var waitFor = entries.Where(e => e.Wait).ToList();
        if (waitFor.Count > 0)
        {
            Output.Step($"waiting for {waitFor.Count} session(s) to finish their sync (timeout {timeout.TotalSeconds:0}s)");
            await WaitSettledAsync(ctx, waitFor, timeout, ct);
        }

        var withExec = entries.Where(e => e.ExecGlobs.Count > 0).ToList();
        if (withExec.Count > 0)
        {
            Output.Step("applying executable bits");
            await ctx.Ssh.RunScriptOrThrowAsync(VmScripts.ApplyExecBits(withExec), privileged: true, "chmod +x", ct: ct);
        }

        Output.Step("bind-mounting synced copies over their 9p paths");
        var mount = await ctx.Ssh.RunScriptOrThrowAsync(VmScripts.Mount(entries), privileged: true, "bind mount", ct: ct);
        var mountLines = mount.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ')).Where(p => p.Length == 2).ToDictionary(p => p[1], p => p[0]);

        foreach (var e in entries)
        {
            var state = mountLines.TryGetValue(e.Id, out var s) ? s : "?";
            Output.Ok($"{e.WindowsPath}  ->  {e.VmTarget}  [{e.Mode.ToMutagen()}, {state}]");
        }

        Output.Ok($"{entries.Count} entry(ies) ready in {sw.Elapsed.TotalSeconds:0.0}s");
        return 0;
    }

    internal static void ReportConfig(Workspace ws)
    {
        Output.Info($"global file:  {ws.GlobalFile}{(File.Exists(ws.GlobalFile) ? "" : " (missing)")}");
        Output.Info($"project file: {ws.ProjectFile ?? "(none)"}");
        foreach (var w in ws.Merged.Warnings) Output.Warn(w);
        foreach (var err in ws.Merged.Errors) Output.Error(err);
    }

    private static async Task EnsureSessionAsync(Context ctx, FileMapEntry e, Dictionary<string, MutagenSession> sessions, CancellationToken ct)
    {
        var beta = $"{ctx.Ssh.Alias}:{e.VmSyncPath}";
        if (sessions.TryGetValue(e.SessionName, out var existing))
        {
            var samePath = string.Equals(existing.AlphaPath.TrimEnd('\\', '/'), e.WindowsPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                           && existing.BetaPath == e.VmSyncPath
                           && existing.BetaHost == ctx.Ssh.Alias;
            var sameMode = existing.Labels.TryGetValue(MutagenCli.ModeLabel, out var m) ? m == e.Mode.ToMutagen() : true;

            if (samePath && sameMode && !existing.IsHalted)
            {
                if (existing.Paused)
                {
                    Output.Step($"resuming session {e.SessionName} ({e.WindowsPath})");
                    var r = await ctx.Mutagen.ResumeAsync(e.SessionName, ct);
                    if (!r.Success) throw new HelperException($"'mutagen sync resume {e.SessionName}' failed:\n{r.Combined}");
                }
                else
                {
                    Output.Debug($"session {e.SessionName} already running ({existing.Summary})");
                }
                return;
            }

            Output.Step($"recreating session {e.SessionName} (configuration changed or session halted: {existing.Summary})");
            await ctx.Mutagen.TerminateAsync(e.SessionName, ct);
        }
        else
        {
            Output.Step($"creating session {e.SessionName}: {e.WindowsPath} -> {beta} ({e.Mode.ToMutagen()})");
        }

        await ctx.Mutagen.CreateAsync(e, ctx.Ssh.Alias, ctx.Machine.Name, ct);
    }

    private static async Task WaitSettledAsync(Context ctx, List<FileMapEntry> entries, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;

        // flush blocks until a full cycle completes; it is the cheapest "initial sync done" signal
        foreach (var e in entries)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;
            var r = await ctx.Mutagen.FlushAsync(e.SessionName, remaining, ct);
            if (r.TimedOut)
            {
                throw new HelperException($"timed out waiting for the first sync of {e.WindowsPath} ({e.SessionName}). " +
                                          "Large trees take a while; raise --timeout (PodmanHelperTimeout in the .dcproj) or run 'podman-helper mount' once by hand.");
            }
            if (!r.Success)
            {
                Output.Warn($"flush of {e.SessionName} reported: {r.Combined}");
            }
        }

        while (true)
        {
            var sessions = (await ctx.Mutagen.ListAsync(ct)).ToDictionary(s => s.Name, StringComparer.Ordinal);
            var pending = new List<string>();
            var failed = new List<string>();

            foreach (var e in entries)
            {
                if (!sessions.TryGetValue(e.SessionName, out var s))
                {
                    failed.Add($"{e.WindowsPath}: session {e.SessionName} disappeared");
                    continue;
                }
                if (s.IsSettled)
                {
                    continue;
                }
                if (s.ConflictCount > 0 || s.IsHalted || !string.IsNullOrEmpty(s.LastError))
                {
                    failed.Add($"{e.WindowsPath}: {s.Summary}  (inspect with: mutagen sync list {e.SessionName})");
                }
                else
                {
                    pending.Add($"{e.WindowsPath}: {s.Summary}");
                }
            }

            if (failed.Count > 0)
            {
                throw new HelperException("sync did not settle:\n  " + string.Join("\n  ", failed));
            }
            if (pending.Count == 0)
            {
                return;
            }
            if (DateTime.UtcNow >= deadline)
            {
                throw new HelperException("timed out waiting for sync to settle:\n  " + string.Join("\n  ", pending));
            }

            foreach (var p in pending) Output.Debug("waiting: " + p);
            await Task.Delay(1000, ct);
        }
    }
}
