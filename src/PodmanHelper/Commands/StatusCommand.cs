using PodmanHelper.Cli;
using PodmanHelper.Config;
using PodmanHelper.Mutagen;
using PodmanHelper.Vm;

namespace PodmanHelper.Commands;

public static class StatusCommand
{
    public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
    {
        var ws = Workspace.Load(cli.Get("project"), requireProject: false);
        MountCommand.ReportConfig(ws);
        if (ws.Entries.Count == 0)
        {
            Output.Info("no entries");
            return ws.Merged.HasErrors ? 2 : 0;
        }

        var ctx = await Context.ConnectAsync(cli, startMachine: false, ct);
        var sessions = (await ctx.Mutagen.ListAsync(ct)).ToDictionary(s => s.Name, StringComparer.Ordinal);

        var vm = await ctx.Ssh.RunScriptAsync(VmScripts.Status(ws.Entries), privileged: false, ct: ct);
        var mounts = new Dictionary<string, (string State, string Size)>(StringComparer.Ordinal);
        foreach (var line in vm.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ');
            if (parts.Length >= 2) mounts[parts[0]] = (parts[1], parts.Length > 2 ? parts[2] : "-");
        }

        Console.WriteLine();
        Console.WriteLine($"{"scope",-8} {"windows path",-45} {"vm target",-40} {"mode",-16} {"mount",-8} {"size",-6} session");
        var allGood = true;
        foreach (var e in ws.Entries)
        {
            var scope = e.IsGlobal ? "global" : "project";
            var mount = mounts.TryGetValue(e.Id, out var m) ? m : (State: "?", Size: "-");
            var session = sessions.TryGetValue(e.SessionName, out var s) ? s.Summary : "no session";
            if (mount.State != "mounted" || s is null || !s.IsSettled) allGood = false;
            Console.WriteLine($"{scope,-8} {Trunc(e.WindowsPath, 45),-45} {Trunc(e.VmTarget, 40),-40} {e.Mode.ToMutagen(),-16} {mount.State,-8} {mount.Size,-6} {session}");
        }
        Console.WriteLine();

        var orphans = sessions.Values.Where(s => ws.Entries.All(e => e.SessionName != s.Name)).ToList();
        foreach (var o in orphans)
        {
            Output.Info($"session {o.Name} ({o.AlphaPath}) is not in the current lists ({o.Summary}); 'podman-helper unmount --all --purge' removes it");
        }

        Output.Info(allGood ? "all entries synced and mounted" : "some entries are not ready; run 'podman-helper mount'");
        return ws.Merged.HasErrors ? 2 : 0;
    }

    private static string Trunc(string s, int width) => s.Length <= width ? s : "…" + s[^(width - 1)..];
}
