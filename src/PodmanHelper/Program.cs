using PodmanHelper.Cli;
using PodmanHelper.Commands;
using PodmanHelper.Mutagen;

var cli = CommandLine.Parse(args);
Output.Verbose = cli.Flag("verbose");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (cli.Flag("version"))
{
    Console.WriteLine($"podman-helper {typeof(Program).Assembly.GetName().Version?.ToString(3)} (bundled mutagen v{MutagenInstaller.Version})");
    return 0;
}

if (cli.Command is null || cli.Flag("help") || cli.Command == "help")
{
    PrintUsage();
    return cli.Command is null && !cli.Flag("help") ? 1 : 0;
}

try
{
    return cli.Command switch
    {
        "init" => await InitCommand.RunAsync(cli, cts.Token),
        "doctor" => await DoctorCommand.RunAsync(cli, cts.Token),
        "mount" => await MountCommand.RunAsync(cli, cts.Token),
        "status" => await StatusCommand.RunAsync(cli, cts.Token),
        "unmount" => await UnmountCommand.RunAsync(cli, cts.Token),
        "flush" => await FlushCommand.RunAsync(cli, cts.Token),
        _ => Unknown(cli.Command),
    };
}
catch (HelperException ex)
{
    Output.Error(ex.Message);
    return ex.ExitCode;
}
catch (OperationCanceledException)
{
    Output.Error("cancelled");
    return 130;
}

static int Unknown(string command)
{
    Output.Error($"unknown command '{command}'");
    PrintUsage();
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
        podman-helper: overlay broken 9p paths in the Podman Hyper-V machine with Mutagen-synced ext4 copies.

        usage: podman-helper <command> [options]

        commands
          init      create the global and project filemap-list.txt and hook the .dcproj
                    [--project <dir>] [--global-only] [--force]
          doctor    check podman, the machine, ssh, mutagen and the lists   [--project <dir>]
          mount     sync every entry and bind-mount it over its 9p path (what Visual Studio runs)
                    [--project <dir>] [--timeout <seconds>] [--no-start]
          status    show sync and mount state per entry                     [--project <dir>]
          unmount   remove the bind mounts and pause the sessions
                    [--project <dir>] [--all] [--purge]
          flush     force a sync cycle and wait for it                      [--project <dir>] [--timeout <seconds>]

        global options
          --machine <name>   podman machine to use (default: the default machine)
          --ssh-user <user>  user for ssh into the machine (default: root on a rootful machine, else the machine user)
          --verbose          print every external command
          --version, --help

        files
          global:  %USERPROFILE%\.podman-helper\filemap-list.txt
          project: <dir with the .dcproj>\filemap-list.txt
        """);
}
