using PodmanHelper.Cli;
using PodmanHelper.Processes;

namespace PodmanHelper.Ssh;

/// <summary>Runs shell scripts inside the podman machine through the managed SSH alias.</summary>
public sealed class SshClient
{
    private readonly IProcessRunner _runner;
    public string SshExe { get; }
    public string Alias { get; }
    /// <summary>The user the alias logs in as; <c>root</c> on a rootful machine, so no sudo is needed.</summary>
    public string User { get; }

    public SshClient(IProcessRunner runner, string alias, string user, string sshExe = "ssh")
    {
        _runner = runner;
        Alias = alias;
        User = user;
        SshExe = sshExe;
    }

    /// <summary>Runs <paramref name="script"/> with <c>bash -s</c> on the machine, through <c>sudo</c> when privileged and not root.</summary>
    public Task<ProcessResult> RunScriptAsync(string script, bool privileged, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var remote = privileged && User != "root" ? "sudo -n bash -s" : "bash -s";
        var spec = new ProcessSpec(SshExe, new[] { "-o", "BatchMode=yes", "-o", "ConnectTimeout=15", Alias, remote })
        {
            StandardInput = "set -e\n" + script.Replace("\r\n", "\n") + "\n",
            Timeout = timeout ?? TimeSpan.FromMinutes(2),
        };
        return _runner.RunAsync(spec, ct);
    }

    public async Task<ProcessResult> RunScriptOrThrowAsync(string script, bool privileged, string what, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var r = await RunScriptAsync(script, privileged, timeout, ct);
        if (!r.Success)
        {
            throw new HelperException($"{what} failed in the podman machine (ssh {Alias}):\n{r.Combined}");
        }
        return r;
    }
}
