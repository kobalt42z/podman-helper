using PodmanHelper.Ssh;
using Xunit;

namespace PodmanHelper.Tests;

public class SshConfigManagerTests
{
    private static readonly string Block = SshConfigManager.BuildBlock("podman-helper-podman-machine-default", "127.0.0.1", 60223, "root", @"C:\Users\shhmouel kessous\.local\share\containers\podman\machine\machine");

    [Fact]
    public void BlockQuotesIdentityFileWithSpaces()
    {
        Assert.Contains("IdentityFile \"C:\\Users\\shhmouel kessous\\.local\\share\\containers\\podman\\machine\\machine\"", Block);
        Assert.Contains("Host podman-helper-podman-machine-default\n", Block);
        Assert.Contains("Port 60223", Block);
    }

    [Fact]
    public void AppendsToExistingConfig()
    {
        var result = SshConfigManager.Upsert("Host other\n  User x", "podman-helper-podman-machine-default", Block);
        Assert.StartsWith("Host other\n  User x\n\n# >>> podman-helper-podman-machine-default", result);
        Assert.EndsWith(Block, result);
    }

    [Fact]
    public void ReplacesExistingBlockInPlace()
    {
        var old = SshConfigManager.BuildBlock("podman-helper-podman-machine-default", "127.0.0.1", 1, "core", "k");
        var existing = "Host a\n\n" + old + "Host z\n";
        var result = SshConfigManager.Upsert(existing, "podman-helper-podman-machine-default", Block);
        Assert.Equal("Host a\n\n" + Block + "Host z\n", result);
        Assert.DoesNotContain("Port 1\n", result);
    }

    [Fact]
    public void ContainsDetectsCrLf()
    {
        Assert.True(SshConfigManager.Contains(Block.Replace("\n", "\r\n"), Block));
        Assert.False(SshConfigManager.Contains(null, Block));
    }
}
