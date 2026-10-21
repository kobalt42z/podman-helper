using System.Text.Json;
using PodmanHelper.Cli;
using PodmanHelper.Processes;

namespace PodmanHelper.Podman;

public sealed record MachineInfo(
    string Name,
    string State,
    string VmType,
    string SshHost,
    int SshPort,
    string SshUser,
    string IdentityPath,
    bool Rootful)
{
    public bool IsRunning => State.Equals("running", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads machine facts from <c>podman machine info / inspect</c> and starts the machine when needed.</summary>
public sealed class PodmanMachine
{
    private readonly IProcessRunner _runner;
    public string PodmanExe { get; }

    public PodmanMachine(IProcessRunner runner, string podmanExe = "podman")
    {
        _runner = runner;
        PodmanExe = podmanExe;
    }

    public async Task<string> VersionAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync(ProcessSpec.Of(PodmanExe, "--version"), ct);
        if (!r.Success) throw new HelperException($"'podman --version' failed: {r.Combined}");
        return r.Stdout.Trim();
    }

    public async Task<MachineInfo> InspectAsync(string? machineName, CancellationToken ct = default)
    {
        var infoResult = await _runner.RunAsync(ProcessSpec.Of(PodmanExe, "machine", "info", "--format", "json"), ct);
        if (!infoResult.Success)
        {
            throw new HelperException($"'podman machine info' failed: {infoResult.Combined}");
        }
        var (vmType, defaultMachine) = ParseInfo(infoResult.Stdout);

        var name = machineName ?? defaultMachine;
        if (string.IsNullOrEmpty(name))
        {
            throw new HelperException("no podman machine found; run 'podman machine init' first");
        }

        var inspect = await _runner.RunAsync(ProcessSpec.Of(PodmanExe, "machine", "inspect", name), ct);
        if (!inspect.Success)
        {
            throw new HelperException($"'podman machine inspect {name}' failed: {inspect.Combined}");
        }

        var host = "127.0.0.1";
        var connections = await _runner.RunAsync(ProcessSpec.Of(PodmanExe, "system", "connection", "list", "--format", "json"), ct);
        if (connections.Success)
        {
            host = ParseConnectionHost(connections.Stdout, name) ?? host;
        }

        return ParseInspect(inspect.Stdout, name, vmType, host);
    }

    public async Task StartAsync(string name, CancellationToken ct = default)
    {
        var r = await _runner.RunAsync(ProcessSpec.Of(PodmanExe, "machine", "start", name) with { Stream = true, Timeout = TimeSpan.FromMinutes(5) }, ct);
        if (!r.Success)
        {
            throw new HelperException($"'podman machine start {name}' failed: {r.Combined}");
        }
    }

    internal static (string VmType, string DefaultMachine) ParseInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var host = doc.RootElement.GetProperty("Host");
        var vmType = host.TryGetProperty("VMType", out var t) ? t.GetString() ?? "" : "";
        var def = host.TryGetProperty("DefaultMachine", out var d) ? d.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(def) && host.TryGetProperty("CurrentMachine", out var c))
        {
            def = c.GetString() ?? "";
        }
        return (vmType, def);
    }

    internal static MachineInfo ParseInspect(string json, string name, string vmType, string host)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var machine = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().First() : root;

        var state = machine.TryGetProperty("State", out var s) ? s.GetString() ?? "" : "";
        var rootful = machine.TryGetProperty("Rootful", out var rf) && rf.ValueKind == JsonValueKind.True;
        var ssh = machine.GetProperty("SSHConfig");
        var identity = ssh.GetProperty("IdentityPath").GetString() ?? "";
        var port = ssh.GetProperty("Port").GetInt32();
        var user = ssh.GetProperty("RemoteUsername").GetString() ?? "";

        return new MachineInfo(name, state, vmType, host, port, user, identity, rootful);
    }

    internal static string? ParseConnectionHost(string json, string machineName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var conn in doc.RootElement.EnumerateArray())
            {
                var cname = conn.TryGetProperty("Name", out var n) ? n.GetString() : null;
                if (cname != machineName) continue;
                var uri = conn.TryGetProperty("URI", out var u) ? u.GetString() : null;
                if (uri is not null && Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                {
                    return parsed.Host;
                }
            }
        }
        catch (JsonException)
        {
            // fall through to the default host
        }
        return null;
    }
}
