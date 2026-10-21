using PodmanHelper.Cli;
using PodmanHelper.Config;
using PodmanHelper.Processes;

namespace PodmanHelper.Mutagen;

/// <summary>Thin wrapper over the <c>mutagen</c> executable with the helper's isolated data directory.</summary>
public sealed class MutagenCli
{
    public const string OwnerLabel = "podman-helper";
    public const string ScopeLabel = "ph-scope";
    public const string MachineLabel = "ph-machine";
    public const string ModeLabel = "ph-mode";

    private readonly IProcessRunner _runner;
    private readonly Dictionary<string, string> _env;
    public string Exe { get; }

    public MutagenCli(IProcessRunner runner, string exe, string dataDirectory, string? sshDirectory)
    {
        _runner = runner;
        Exe = exe;
        _env = new Dictionary<string, string>
        {
            // Keep our sessions and daemon apart from any Mutagen the user installed themselves.
            ["MUTAGEN_DATA_DIRECTORY"] = dataDirectory,
        };
        if (sshDirectory is not null)
        {
            _env["MUTAGEN_SSH_PATH"] = sshDirectory;
        }
    }

    public IReadOnlyDictionary<string, string> Environment => _env;

    private ProcessSpec Spec(params string[] args) => new(Exe, args) { Environment = _env };

    public async Task<string> VersionAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync(Spec("version"), ct);
        if (!r.Success) throw new HelperException($"'mutagen version' failed: {r.Combined}");
        return r.Stdout.Trim();
    }

    public async Task DaemonStartAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync(Spec("daemon", "start"), ct);
        if (!r.Success) throw new HelperException($"'mutagen daemon start' failed: {r.Combined}");
    }

    public async Task DaemonStopAsync(CancellationToken ct = default)
    {
        await _runner.RunAsync(Spec("daemon", "stop"), ct);
    }

    public async Task<List<MutagenSession>> ListAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync(Spec("sync", "list", "--label-selector", OwnerLabel, "--template", "{{json .}}"), ct);
        if (!r.Success)
        {
            if (r.Combined.Contains("no sessions", StringComparison.OrdinalIgnoreCase) ||
                r.Combined.Contains("No synchronization sessions", StringComparison.OrdinalIgnoreCase))
            {
                return new List<MutagenSession>();
            }
            throw new HelperException($"'mutagen sync list' failed: {r.Combined}");
        }
        return MutagenSession.ParseList(r.Stdout);
    }

    public async Task CreateAsync(FileMapEntry entry, string sshAlias, string machineName, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "sync", "create",
            "--name", entry.SessionName,
            "--label", $"{OwnerLabel}=1",
            "--label", $"{ScopeLabel}={ScopeId.For(entry.Scope)}",
            "--label", $"{MachineLabel}={machineName}",
            "--label", $"{ModeLabel}={entry.Mode.ToMutagen()}",
            "--sync-mode", entry.Mode.ToMutagen(),
            "--symlink-mode", "portable",
            "--ignore-vcs",
            // Readable by any uid inside containers; executability is handled separately (x= option).
            "--default-file-mode-beta", "0644",
            "--default-directory-mode-beta", "0755",
        };
        foreach (var ignore in entry.Ignores)
        {
            args.Add("--ignore");
            args.Add(ignore);
        }
        args.Add(entry.WindowsPath);
        args.Add($"{sshAlias}:{entry.VmSyncPath}");

        var r = await _runner.RunAsync(new ProcessSpec(Exe, args) { Environment = _env, Timeout = TimeSpan.FromMinutes(3) }, ct);
        if (!r.Success)
        {
            throw new HelperException($"'mutagen sync create {entry.SessionName}' failed for {entry.WindowsPath}:\n{r.Combined}");
        }
    }

    public Task<ProcessResult> ResumeAsync(string name, CancellationToken ct = default) =>
        _runner.RunAsync(Spec("sync", "resume", name) with { Timeout = TimeSpan.FromMinutes(3) }, ct);

    public Task<ProcessResult> PauseAsync(string name, CancellationToken ct = default) =>
        _runner.RunAsync(Spec("sync", "pause", name), ct);

    public Task<ProcessResult> TerminateAsync(string name, CancellationToken ct = default) =>
        _runner.RunAsync(Spec("sync", "terminate", name), ct);

    /// <summary>Forces a cycle and blocks until it completes (or the timeout hits).</summary>
    public Task<ProcessResult> FlushAsync(string name, TimeSpan timeout, CancellationToken ct = default) =>
        _runner.RunAsync(Spec("sync", "flush", name) with { Timeout = timeout }, ct);

    public Task<ProcessResult> ResetAsync(string name, CancellationToken ct = default) =>
        _runner.RunAsync(Spec("sync", "reset", name), ct);
}
