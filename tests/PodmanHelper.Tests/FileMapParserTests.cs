using PodmanHelper.Config;
using Xunit;

namespace PodmanHelper.Tests;

public class FileMapParserTests
{
    private static string? Env(string name) => name == "USERPROFILE" ? @"C:\Users\me" : null;

    private static FileMapParseResult Parse(string text, string scope = FileMapEntry.GlobalScope, string baseDir = @"C:\Users\me\.podman-helper") =>
        FileMapParser.Parse(text, baseDir, "test.txt", scope, Env);

    [Fact]
    public void ParsesEntriesWithOptionsAndComments()
    {
        var r = Parse("""
            # comment line
            ~\vsdbg    mode=one-way-replica  x=**/vsdbg,**/*.sh   # trailing comment
            "C:\Program Files\tool" mode=two-way-safe ignore=*.log,**/tmp wait=false target=/opt/tool/
            .\rel
            """);

        Assert.Empty(r.Errors);
        Assert.Equal(3, r.Entries.Count);

        var vsdbg = r.Entries[0];
        Assert.Equal(@"C:\Users\me\vsdbg", vsdbg.WindowsPath);
        Assert.Equal(SyncMode.OneWayReplica, vsdbg.Mode);
        Assert.Equal(new[] { "**/vsdbg", "**/*.sh" }, vsdbg.ExecGlobs);
        Assert.True(vsdbg.Wait);
        Assert.Equal("/mnt/c/Users/me/vsdbg", vsdbg.VmTarget);
        Assert.Equal("ph-" + vsdbg.Id, vsdbg.SessionName);
        Assert.Equal(2, vsdbg.Line);

        var tool = r.Entries[1];
        Assert.Equal(@"C:\Program Files\tool", tool.WindowsPath);
        Assert.Equal(SyncMode.TwoWaySafe, tool.Mode);
        Assert.Equal(new[] { "*.log", "**/tmp" }, tool.Ignores);
        Assert.False(tool.Wait);
        Assert.Equal("/opt/tool", tool.VmTarget);

        Assert.Equal(@"C:\Users\me\.podman-helper\rel", r.Entries[2].WindowsPath);
    }

    [Fact]
    public void ParsesExclusions()
    {
        var r = Parse("!~\\.nuget\\packages\n");
        Assert.Empty(r.Errors);
        Assert.Empty(r.Entries);
        Assert.Equal(new[] { @"C:\Users\me\.nuget\packages" }, r.Exclusions);
    }

    [Theory]
    [InlineData("~\\x mode=sideways", "unknown mode")]
    [InlineData("~\\x colour=red", "unknown option")]
    [InlineData("~\\x wait=maybe", "wait must be")]
    [InlineData("~\\x target=relative", "absolute VM path")]
    [InlineData("~\\x junk", "expected key=value")]
    [InlineData("\"~\\x", "unbalanced")]
    [InlineData("!~\\x mode=two-way-safe", "not allowed on a '!'")]
    [InlineData("\\\\server\\share", "UNC")]
    public void ReportsErrorsWithLineNumbers(string line, string expected)
    {
        var r = Parse(line);
        var error = Assert.Single(r.Errors);
        Assert.StartsWith("test.txt:1:", error);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void TokenizerHandlesQuotes()
    {
        Assert.Equal(new[] { @"C:\a b\c", "mode=x" }, FileMapParser.Tokenize("\"C:\\a b\\c\" mode=x"));
        Assert.Equal(new[] { "a", "b" }, FileMapParser.Tokenize("  a \t b  "));
    }
}
