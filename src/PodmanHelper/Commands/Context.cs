using PodmanHelper.Cli;
using PodmanHelper.Mutagen;
using PodmanHelper.Podman;
using PodmanHelper.Processes;
using PodmanHelper.Ssh;

namespace PodmanHelper.Commands;

/// <summary>Everything the machine-facing commands share: podman, the SSH alias, and the Mutagen CLI.</summary>
public sealed class Context
{
    public required IProcessRunner Runner { get; init; }
    public required PodmanMachine Podman { get; init; }
    public required MachineInfo Machine { get; init; }
    public required SshClient Ssh { get; init; }
    public required MutagenCli Mutagen { get; init; }

    /// <summary>Resolves the machine, makes sure it runs, writes the SSH alias and prepares Mutagen.</summary>
    public static async Task<Context> ConnectAsync(CommandLine cli, bool startMachine, CancellationToken ct)
    {
        var runner = new ProcessRunner();
        var podman = new PodmanMachine(runner);

        Output.Step("reading podman machine configuration");
        var machine = await podman.InspectAsync(cli.Get("machine"), ct);
        Output.Info($"machine {machine.Name} ({machine.VmType}), state {machine.State}, ssh {machine.SshUser}@{machine.SshHost}:{machine.SshPort}");

        if (!machine.IsRunning)
        {
            if (!startMachine)
            {
                throw new HelperException($"podman machine '{machine.Name}' is not running (state: {machine.State})");
            }
            Output.Step($"starting podman machine {machine.Name}");
            await podman.StartAsync(machine.Name, ct);
            machine = await podman.InspectAsync(machine.Name, ct);
            if (!machine.IsRunning)
            {
                throw new HelperException($"podman machine '{machine.Name}' did not reach the running state (state: {machine.State})");
            }
        }

        var user = SshUserFor(machine, cli.Get("ssh-user"));
        var alias = SshConfigManager.AliasFor(machine.Name);
        var configPath = SshConfigManager.DefaultConfigPath();
        if (SshConfigManager.EnsureAlias(configPath, alias, machine.SshHost, machine.SshPort, user, machine.IdentityPath))
        {
            Output.Ok($"wrote ssh alias {alias} ({user}@{machine.SshHost}:{machine.SshPort}) to {configPath}");
        }

        var sshExe = FindSsh();
        var ssh = new SshClient(runner, alias, user, sshExe);

        var mutagenExe = MutagenInstaller.Ensure();
        var mutagen = new MutagenCli(runner, mutagenExe, MutagenInstaller.DataDirectory(), Path.GetDirectoryName(sshExe));

        return new Context { Runner = runner, Podman = podman, Machine = machine, Ssh = ssh, Mutagen = mutagen };
    }

    /// <summary>
    /// A rootful machine has a root SSH connection with the same key (podman creates "name-root"), which
    /// saves the sudo round trip; a rootless machine uses the machine user and sudo -n.
    /// </summary>
    public static string SshUserFor(MachineInfo machine, string? overrideUser) =>
        overrideUser ?? (machine.Rootful ? "root" : machine.SshUser);

    /// <summary>Full path of the OpenSSH client; Windows' built-in one by preference so Mutagen and we use the same.</summary>
    public static string FindSsh()
    {
        var name = OperatingSystem.IsWindows() ? "ssh.exe" : "ssh";
        if (OperatingSystem.IsWindows())
        {
            var builtIn = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "OpenSSH", "ssh.exe");
            if (File.Exists(builtIn)) return builtIn;
        }
        return MutagenInstaller.FindOnPath(name)
            ?? throw new HelperException("OpenSSH client (ssh.exe) not found. Install the Windows optional feature 'OpenSSH Client' or Git for Windows.");
    }
}
