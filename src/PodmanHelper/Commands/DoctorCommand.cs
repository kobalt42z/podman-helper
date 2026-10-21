using PodmanHelper.Cli;
using PodmanHelper.Config;
using PodmanHelper.Mutagen;
using PodmanHelper.Podman;
using PodmanHelper.Processes;
using PodmanHelper.Ssh;
using PodmanHelper.Vm;

namespace PodmanHelper.Commands;

/// <summary>Checks every prerequisite and prints what to fix. Exit 1 when something is broken.</summary>
public static class DoctorCommand
{
    public static async Task<int> RunAsync(CommandLine cli, CancellationToken ct)
    {
        var problems = 0;
        void Fail(string what, string fix)
        {
            problems++;
            Output.Warn(what);
            Output.Info("fix: " + fix);
        }

        var runner = new ProcessRunner();

        if (!OperatingSystem.IsWindows())
        {
            Output.Warn("not running on Windows; the mount commands target the Podman Hyper-V machine on Windows");
        }

        // config files
        var globalFile = Workspace.DefaultGlobalFile();
        if (File.Exists(globalFile)) Output.Ok($"global file: {globalFile}");
        else Fail($"global file missing: {globalFile}", "podman-helper init");

        var projectDir = Workspace.FindProjectDirectory(cli.Get("project") ?? Directory.GetCurrentDirectory());
        if (projectDir is null) Output.Info("no project (no filemap-list.txt or .dcproj above the current directory)");
        else
        {
            var pf = Path.Combine(projectDir, Workspace.FileName);
            if (File.Exists(pf)) Output.Ok($"project file: {pf}");
            else Fail($"project file missing in {projectDir}", $"podman-helper init --project \"{projectDir}\"");
            var targets = Path.Combine(projectDir, Workspace.TargetsFileName);
            if (File.Exists(targets)) Output.Ok($"msbuild hook: {targets}");
            else Output.Info($"no {Workspace.TargetsFileName} in {projectDir} (init adds it next to the .dcproj)");
        }

        var ws = Workspace.Load(cli.Get("project"), requireProject: false);
        foreach (var w in ws.Merged.Warnings) Output.Warn(w);
        foreach (var e in ws.Merged.Errors) { problems++; Output.Warn(e); }
        foreach (var e in ws.Entries)
        {
            if (Directory.Exists(e.WindowsPath)) Output.Ok($"entry {e.WindowsPath} -> {e.VmTarget}");
            else Fail($"entry path does not exist: {e.WindowsPath} ({e.SourceFile}:{e.Line})", "create it or fix the line");
        }

        // ssh client
        string? sshExe = null;
        try
        {
            sshExe = Context.FindSsh();
            var v = await runner.RunAsync(ProcessSpec.Of(sshExe, "-V"), ct);
            Output.Ok($"ssh: {sshExe} ({v.Combined.Trim()})");
        }
        catch (HelperException ex)
        {
            Fail(ex.Message, "Settings > Apps > Optional features > OpenSSH Client");
        }

        // mutagen
        try
        {
            var exe = MutagenInstaller.Ensure();
            var mutagen = new MutagenCli(runner, exe, MutagenInstaller.DataDirectory(), sshExe is null ? null : Path.GetDirectoryName(sshExe));
            var version = await mutagen.VersionAsync(ct);
            Output.Ok($"mutagen: {exe} (v{version}, bundled v{MutagenInstaller.Version}, data dir {MutagenInstaller.DataDirectory()})");
        }
        catch (HelperException ex)
        {
            Fail(ex.Message, "reinstall podman-helper (the bundled Mutagen is extracted on first use)");
        }

        // podman + machine
        MachineInfo? machine = null;
        try
        {
            var podman = new PodmanMachine(runner);
            Output.Ok(await podman.VersionAsync(ct));
            machine = await podman.InspectAsync(cli.Get("machine"), ct);
            Output.Ok($"machine {machine.Name}: {machine.VmType}, {machine.State}, ssh {machine.SshUser}@{machine.SshHost}:{machine.SshPort}, key {machine.IdentityPath}");
            if (!machine.VmType.Equals("hyperv", StringComparison.OrdinalIgnoreCase))
            {
                Output.Warn($"machine type is '{machine.VmType}', the helper was designed for hyperv (it should still work when the machine mounts Windows drives under /mnt)");
            }
            if (!File.Exists(machine.IdentityPath)) Fail($"ssh identity file missing: {machine.IdentityPath}", "podman machine rm + init");
            if (!machine.IsRunning) Fail($"machine is {machine.State}", $"podman machine start {machine.Name}");
        }
        catch (HelperException ex)
        {
            Fail(ex.Message, "install Podman and run 'podman machine init'");
        }

        if (machine is { IsRunning: true } && sshExe is not null)
        {
            var user = Context.SshUserFor(machine, cli.Get("ssh-user"));
            var alias = SshConfigManager.AliasFor(machine.Name);
            SshConfigManager.EnsureAlias(SshConfigManager.DefaultConfigPath(), alias, machine.SshHost, machine.SshPort, user, machine.IdentityPath);
            var ssh = new SshClient(runner, alias, user, sshExe);
            var r = await ssh.RunScriptAsync(VmScripts.Doctor, privileged: false, TimeSpan.FromSeconds(40), ct);
            if (!r.Success)
            {
                Fail($"ssh to the machine via alias '{alias}' failed: {r.Combined}", $"check {SshConfigManager.DefaultConfigPath()} and 'podman machine ssh'");
            }
            else
            {
                var facts = r.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
                Output.Ok($"ssh alias {alias} works: {facts.GetValueOrDefault("os")}, user {facts.GetValueOrDefault("user")}");
                if (user != "root" && facts.GetValueOrDefault("sudo") != "ok") Fail("passwordless sudo is not available in the machine", "the helper needs it for mount --bind (or use --ssh-user root on a rootful machine)");
                var fs = facts.GetValueOrDefault("mnt_c", "none");
                if (fs == "none") Output.Warn("/mnt/c is not a mount in the machine; the Windows drive may be mounted elsewhere (use target= on entries)");
                else Output.Ok($"/mnt/c is {fs}, free space under /var/lib: {facts.GetValueOrDefault("varlib_free")}");
                foreach (var tool in new[] { "findmnt", "flock", "mountpoint", "scp" })
                {
                    if (facts.GetValueOrDefault("tool_" + tool) != "ok") Fail($"{tool} missing in the machine", "unexpected for Fedora CoreOS; report it");
                }
            }
        }

        Console.WriteLine();
        if (problems == 0) Output.Ok("everything looks fine");
        else Output.Warn($"{problems} problem(s) found");
        return problems == 0 ? 0 : 1;
    }
}
