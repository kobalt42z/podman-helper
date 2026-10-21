using PodmanHelper.Config;
using Xunit;

namespace PodmanHelper.Tests;

public class WindowsPathTests
{
    private static string? Env(string name) => name switch
    {
        "USERPROFILE" => @"C:\Users\shhmouel kessous",
        "TEMP" => @"C:\Temp",
        _ => null,
    };

    [Theory]
    [InlineData(@"~\vsdbg", @"C:\Users\shhmouel kessous\vsdbg")]
    [InlineData("~/vsdbg/", @"C:\Users\shhmouel kessous\vsdbg")]
    [InlineData(@"%TEMP%\x", @"C:\Temp\x")]
    [InlineData(@".\src\Api\bin", @"D:\work\proj\src\Api\bin")]
    [InlineData(@"..\shared", @"D:\work\shared")]
    [InlineData(@"d:/other/./a/../b", @"D:\other\b")]
    [InlineData(@"C:\", @"C:\")]
    public void Normalizes(string raw, string expected)
    {
        Assert.Equal(expected, WindowsPath.Normalize(raw, @"D:\work\proj", Env));
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData(@"C:relative")]
    public void Rejects(string raw)
    {
        Assert.Throws<FormatException>(() => WindowsPath.Normalize(raw, @"D:\work\proj", Env));
    }

    [Fact]
    public void IsInsideIsCaseInsensitiveAndStrict()
    {
        Assert.True(WindowsPath.IsInside(@"C:\Users\me", @"c:\users\ME\vsdbg"));
        Assert.False(WindowsPath.IsInside(@"C:\Users\me", @"C:\Users\me"));
        Assert.False(WindowsPath.IsInside(@"C:\Users\me", @"C:\Users\meow"));
    }

    [Theory]
    [InlineData(@"C:\Users\shhmouel kessous\vsdbg", "/mnt/c/Users/shhmouel kessous/vsdbg")]
    [InlineData(@"D:\", "/mnt/d")]
    public void MapsToVmPath(string windows, string vm)
    {
        Assert.Equal(vm, VmPaths.FromWindows(windows));
    }

    [Fact]
    public void EntryIdIsStableAndCaseInsensitive()
    {
        Assert.Equal(EntryId.For(@"C:\Users\me\vsdbg"), EntryId.For(@"c:\users\ME\vsdbg"));
        Assert.Equal(12, EntryId.For(@"C:\x").Length);
    }

    [Fact]
    public void ShellQuoteEscapesSingleQuotes()
    {
        Assert.Equal("'/Users/it'\\''s here'", VmPaths.ShellQuote("/Users/it's here"));
    }
}
