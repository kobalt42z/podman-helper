using PodmanHelper.Config;
using Xunit;

namespace PodmanHelper.Tests;

public class FileMapMergerTests
{
    private static string? Env(string name) => name == "USERPROFILE" ? @"C:\Users\me" : null;

    private static FileMapParseResult Global(string text) =>
        FileMapParser.Parse(text, @"C:\Users\me\.podman-helper", "global.txt", FileMapEntry.GlobalScope, Env);

    private static FileMapParseResult Project(string text) =>
        FileMapParser.Parse(text, @"D:\work\proj", "project.txt", @"d:\work\proj", Env);

    [Fact]
    public void UnionWithProjectOverrideAndExclusion()
    {
        var merged = FileMapMerger.Merge(
            Global("~\\vsdbg x=**/vsdbg\n~\\.nuget\\packages\n~\\tools\n"),
            Project("!~\\.nuget\\packages\n~\\tools mode=two-way-safe\n.\\data\n"));

        Assert.Empty(merged.Errors);
        Assert.Equal(new[] { @"C:\Users\me\vsdbg", @"C:\Users\me\tools", @"D:\work\proj\data" }, merged.Entries.Select(e => e.WindowsPath));

        var tools = merged.Entries[1];
        Assert.Equal(SyncMode.TwoWaySafe, tools.Mode);
        Assert.False(tools.IsGlobal);
        Assert.Contains(merged.Warnings, w => w.Contains("overrides the global entry"));
    }

    [Fact]
    public void NestedEntriesAreAnError()
    {
        var merged = FileMapMerger.Merge(Global("~\\vsdbg\n"), Project("~\\vsdbg\\vs2017u5\n"));
        Assert.Contains(merged.Errors, e => e.Contains("nested entries are not allowed"));
    }

    [Fact]
    public void DuplicateInSameFileIsAnError()
    {
        var merged = FileMapMerger.Merge(Global("~\\vsdbg\n~/VSDBG\n"), null);
        Assert.Contains(merged.Errors, e => e.Contains("duplicate entry"));
    }

    [Fact]
    public void SameTargetIsAnError()
    {
        var merged = FileMapMerger.Merge(Global("~\\a target=/opt/x\n~\\b target=/opt/x\n"), null);
        Assert.Contains(merged.Errors, e => e.Contains("VM target /opt/x is also used"));
    }

    [Fact]
    public void UnmatchedExclusionIsAWarning()
    {
        var merged = FileMapMerger.Merge(Global(""), Project("!~\\nothing\n"));
        Assert.Empty(merged.Errors);
        Assert.Contains(merged.Warnings, w => w.Contains("does not match any global entry"));
    }
}
