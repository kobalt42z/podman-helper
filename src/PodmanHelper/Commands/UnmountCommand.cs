using PodmanHelper.Cli;
using PodmanHelper.Config;
using PodmanHelper.Vm;

namespace PodmanHelper.Commands;

/// <summary>
/// Takes the bind mounts off and pauses the Mutagen sessions. Default scope: this project's entries.
/// <c>--all</c> includes global entries and sessions from other projects; <c>--purge</c> terminates the
/// sessions and deletes the copies in the machine.
/// </summary>
public static class UnmountCommand
{
    public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
    {
        var all = cli.Flag("all");
        var purge = cli.Flag("purge");
        var ws = Workspace.Load(cli.Get("project"), requireProject: !all);
        MountCommand.ReportConfig(ws);

        var entries = (all ? ws.Entries : ws.ProjectEntries).ToList();
        var ctx = await Context.ConnectAsync(cli, startMachine: false, ct);

        if (entries.Count > 0)
        {
            Output.Step($"unmounting {entries.Count} entry(ies){(purge ? " and deleting their copies" : "")}");
            var r = await ctx.Ssh.RunScriptOrThrowAsync(VmScripts.Unmount(entries, purge), privileged: true, "unmount", ct: ct);
            foreach (var line in r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Output.Ok(line);
            }
        }

        var sessions = await ctx.Mutagen.ListAsync(ct);
        var names = new HashSet<string>(entries.Select(e => e.SessionName), StringComparer.Ordinal);
        if (all)
        {
            foreach (var s in sessions) names.Add(s.Name);
        }

        foreach (var s in sessions.Where(s => names.Contains(s.Name)))
        {
            if (purge)
            {
                Output.Step($"terminating session {s.Name} ({s.AlphaPath})");
                await ctx.Mutagen.TerminateAsync(s.Name, ct);
            }
            else if (!s.Paused)
            {
                Output.Step($"pausing session {s.Name} ({s.AlphaPath})");
                await ctx.Mutagen.PauseAsync(s.Name, ct);
            }
        }

        Output.Ok("done");
        return 0;
    }
}
