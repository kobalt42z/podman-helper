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
