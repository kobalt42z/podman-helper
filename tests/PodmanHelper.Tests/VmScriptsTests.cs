using PodmanHelper.Config;
using PodmanHelper.Vm;
using Xunit;

namespace PodmanHelper.Tests;

public class VmScriptsTests
{
    private static FileMapEntry Entry(string path, params string[] exec) => new()
    {
        WindowsPath = path,
        ExecGlobs = exec,
        Scope = FileMapEntry.GlobalScope,
        SourceFile = "t",
        Line = 1,
    };

    [Theory]
    [InlineData("**/vsdbg", "-path '*/vsdbg'")]
    [InlineData("**/*.sh", "-path '*/*.sh'")]
    [InlineData("bin/tool", "-path '/var/lib/podman-helper/sync/x/bin/tool'")]
    [InlineData("bin\\**\\*.exe", "-path '/var/lib/podman-helper/sync/x/bin/*/*.exe'")]
    public void TranslatesGlobs(string glob, string expected)
    {
        Assert.Equal(expected, VmScripts.GlobToFindTest("/var/lib/podman-helper/sync/x", glob));
    }

    [Fact]
    public void MountScriptQuotesPathsWithSpaces()
    {
        var e = Entry(@"C:\Users\shhmouel kessous\vsdbg");
        var script = VmScripts.Mount(new[] { e });
        Assert.Contains("mount --bind '/var/lib/podman-helper/sync/" + e.Id + "' '/mnt/c/Users/shhmouel kessous/vsdbg'", script);
        Assert.Contains("mkdir -p '/mnt/c/Users/shhmouel kessous/vsdbg'", script);
    }

    [Fact]
    public void ExecScriptOnlyForEntriesWithGlobs()
    {
        var script = VmScripts.ApplyExecBits(new[] { Entry(@"C:\a"), Entry(@"C:\b", "**/vsdbg") });
        Assert.Single(script.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("chmod a+x", script);
        Assert.Contains("-path '*/vsdbg'", script);
    }

    [Fact]
    public void UnmountWithPurgeRemovesCopy()
    {
        var e = Entry(@"C:\a");
        Assert.Contains("rm -rf '/var/lib/podman-helper/sync/" + e.Id + "'", VmScripts.Unmount(new[] { e }, purge: true));
        Assert.DoesNotContain("rm -rf", VmScripts.Unmount(new[] { e }, purge: false));
    }
}

public class FileEntryTests
{
    private static FileMapEntry FileEntry(string path) => new()
    {
        WindowsPath = path,
        IsFile = true,
        Scope = FileMapEntry.GlobalScope,
        SourceFile = "t",
        Line = 1,
    };

    [Fact]
    public void FileEntrySyncsIntoItsOwnDirectory()
    {
        var e = FileEntry(@"C:\Users\shhmouel kessous\myuser.keytab");
        Assert.Equal("myuser.keytab", e.FileName);
        Assert.Equal(VmPaths.SyncRoot + "/" + e.Id + "/myuser.keytab", e.VmSyncPath);
        Assert.Equal("/mnt/c/Users/shhmouel kessous/myuser.keytab", e.VmTarget);
    }

    [Fact]
    public void MountScriptForFileTouchesInsteadOfMkdir()
    {
        var e = FileEntry(@"C:\Users\me\myuser.keytab");
        var script = VmScripts.Mount(new[] { e });
        Assert.Contains("touch '/mnt/c/Users/me/myuser.keytab'", script);
        Assert.DoesNotContain("mkdir -p '/mnt/c/Users/me/myuser.keytab'", script);
        Assert.Contains("mount --bind '" + e.VmSyncPath + "' '/mnt/c/Users/me/myuser.keytab'", script);
    }

    [Fact]
    public void ScriptsAreValidBash()
    {
        if (!File.Exists("/bin/bash")) return;
        var entries = new FileMapEntry[]
        {
            FileEntry(@"C:\Users\it's me\a.keytab"),
            new() { WindowsPath = @"C:\Users\me\vsdbg", ExecGlobs = new[] { "**/vsdbg" }, Scope = FileMapEntry.GlobalScope, SourceFile = "t", Line = 2 },
        };
        foreach (var script in new[]
                 {
                     VmScripts.Prepare(entries), VmScripts.ApplyExecBits(entries), VmScripts.Mount(entries),
                     VmScripts.Status(entries), VmScripts.Unmount(entries, purge: true), VmScripts.Doctor,
                 })
        {
            var psi = new System.Diagnostics.ProcessStartInfo("/bin/bash", "-n") { RedirectStandardInput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.StandardInput.Write(script);
            p.StandardInput.Close();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.True(p.ExitCode == 0, err + "\n" + script);
        }
    }
}
