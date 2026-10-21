using PodmanHelper.Podman;
using Xunit;

namespace PodmanHelper.Tests;

public class PodmanMachineTests
{
    // Shape of `podman machine inspect` on Podman 6.1 / Hyper-V (values from a real machine).
    private const string Inspect = """
        [
          {
            "ConfigDir": { "Path": "C:\\Users\\shhmouel kessous\\.config\\containers\\podman\\machine\\hyperv" },
            "ConnectionInfo": { "PodmanSocket": { "Path": "C:\\Users\\SHHMOU~1\\AppData\\Local\\Temp\\podman\\podman-machine-default-api.sock" }, "PodmanPipe": { "Path": "\\\\.\\pipe\\podman-machine-default" } },
            "Created": "2026-10-21T13:26:52+03:00",
            "LastUp": "2026-10-21T13:36:17+03:00",
            "Name": "podman-machine-default",
            "Resources": { "CPUs": 4, "DiskSize": 100, "Memory": 2048, "USBs": [] },
            "SSHConfig": { "IdentityPath": "C:\\Users\\shhmouel kessous\\.local\\share\\containers\\podman\\machine\\machine", "Port": 60223, "RemoteUsername": "core" },
            "State": "running",
            "UserModeNetworking": true,
            "Rootful": true,
            "Rosetta": false
          }
        ]
        """;

    private const string Info = """
        { "Host": { "Arch": "amd64", "CurrentMachine": "podman-machine-default", "DefaultMachine": "", "MachineState": "Running", "NumberOfMachines": 1, "OS": "windows", "VMType": "hyperv" },
          "Version": { "Version": "6.1.4" } }
        """;

    private const string Connections = """
        [
          { "Name": "podman-machine-default", "URI": "ssh://core@127.0.0.1:60223/run/user/1000/podman/podman.sock", "Identity": "x", "IsMachine": true, "Default": false },
          { "Name": "podman-machine-default-root", "URI": "ssh://root@127.0.0.1:60223/run/podman/podman.sock", "Identity": "x", "IsMachine": true, "Default": true }
        ]
        """;

    [Fact]
    public void ParsesInspect()
    {
        var m = PodmanMachine.ParseInspect(Inspect, "podman-machine-default", "hyperv", "127.0.0.1");
        Assert.True(m.IsRunning);
        Assert.True(m.Rootful);
        Assert.Equal(60223, m.SshPort);
        Assert.Equal("core", m.SshUser);
        Assert.Equal(@"C:\Users\shhmouel kessous\.local\share\containers\podman\machine\machine", m.IdentityPath);
    }

    [Fact]
    public void InfoFallsBackToCurrentMachine()
    {
        var (vmType, def) = PodmanMachine.ParseInfo(Info);
        Assert.Equal("hyperv", vmType);
        Assert.Equal("podman-machine-default", def);
    }

    [Fact]
    public void ReadsHostFromConnectionList()
    {
        Assert.Equal("127.0.0.1", PodmanMachine.ParseConnectionHost(Connections, "podman-machine-default"));
        Assert.Null(PodmanMachine.ParseConnectionHost(Connections, "other"));
        Assert.Null(PodmanMachine.ParseConnectionHost("not json", "x"));
    }
}
