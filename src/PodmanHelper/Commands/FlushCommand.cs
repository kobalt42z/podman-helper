using PodmanHelper.Cli;
using PodmanHelper.Config;

namespace PodmanHelper.Commands;

/// <summary>Forces a sync cycle for the project's sessions and waits for it.</summary>
public static class FlushCommand
{
    public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
    {
        var ws = Workspace.Load(cli.Get("project"), requireProject: true);
        MountCommand.ReportConfig(ws);
        if (ws.Merged.HasErrors) return 2;

        var timeout = TimeSpan.FromSeconds(Math.Max(10, cli.GetInt("timeout", 180)));
        var ctx = await Context.ConnectAsync(cli, startMachine: false, ct);
        var sessions = (await ctx.Mutagen.ListAsync(ct)).ToDictionary(s => s.Name, StringComparer.Ordinal);

        var failed = 0;
        foreach (var e in ws.Entries)
        {
            if (!sessions.ContainsKey(e.SessionName))
            {
                Output.Warn($"{e.WindowsPath}: no session yet (run 'podman-helper mount')");
                failed++;
                continue;
            }
            Output.Step($"flushing {e.WindowsPath}");
            var r = await ctx.Mutagen.FlushAsync(e.SessionName, timeout, ct);
            if (r.TimedOut || !r.Success)
            {
                Output.Error($"{e.WindowsPath}: {(r.TimedOut ? "timed out" : r.Combined)}");
                failed++;
            }
        }

        if (failed == 0) Output.Ok("all sessions flushed");
        return failed == 0 ? 0 : 1;
    }
}
