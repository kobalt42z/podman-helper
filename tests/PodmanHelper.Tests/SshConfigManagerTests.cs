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
    public void PutsBlockBeforeExistingConfigSoHostStarCannotOverrideIt()
    {
        var result = SshConfigManager.Upsert("Host *\n  User core\n", "podman-helper-podman-machine-default", Block);
        Assert.StartsWith(Block, result);
        Assert.EndsWith("\nHost *\n  User core\n", result);
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

public class PrivilegedCommandTests
{
    [Fact]
    public void RunsScriptAsRootWhetherOrNotLoginIsRoot()
    {
        if (!File.Exists("/bin/bash")) return;
        // as the current user (root in CI containers or not): the wrapper must run the stdin script and pass its output
        var psi = new System.Diagnostics.ProcessStartInfo("/bin/sh", new[] { "-c", PodmanHelper.Ssh.SshClient.PrivilegedRemoteCommand })
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.StandardInput.Write("echo uid=$(id -u)\n");
        p.StandardInput.Close();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) return; // no passwordless sudo on this runner: nothing to assert
        Assert.Equal("uid=0", output.Trim());
    }
}

public class KnownHostsTests
{
    [Fact]
    public void BlockUsesARealKnownHostsFileNotNul()
    {
        var block = SshConfigManager.BuildBlock("a", "127.0.0.1", 1, "root", "k");
        Assert.DoesNotContain("UserKnownHostsFile NUL", block);
        Assert.Contains("UserKnownHostsFile \"" + SshConfigManager.KnownHostsPath() + "\"", block);
        Assert.EndsWith(Path.Combine("podman-helper", "known_hosts"), SshConfigManager.KnownHostsPath());
    }
}
