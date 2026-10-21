using PodmanHelper.Cli;
using PodmanHelper.Commands;
using Xunit;

namespace PodmanHelper.Tests;

public class InitCommandTests
{
    private const string Dcproj = "<Project ToolsVersion=\"15.0\" Sdk=\"Microsoft.Docker.Sdk\">\r\n  <PropertyGroup Label=\"Globals\">\r\n    <ProjectVersion>2.1</ProjectVersion>\r\n  </PropertyGroup>\r\n</Project>";

    [Fact]
    public void AddsImportBeforeClosingTagKeepingLineEndings()
    {
        var result = InitCommand.AddImport(Dcproj)!;
        Assert.EndsWith("\r\n\r\n  " + InitCommand.ImportLine + "\r\n</Project>", result);
        Assert.StartsWith("<Project ToolsVersion", result);
        Assert.Null(InitCommand.AddImport(result));
    }

    [Fact]
    public void RejectsFileWithoutProjectElement()
    {
        Assert.Throws<HelperException>(() => InitCommand.AddImport("<Foo/>"));
    }

    [Fact]
    public void TargetsResourceIsValidXmlWithTheHook()
    {
        var xml = InitCommand.Resource("podman-helper.targets");
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var target = doc.Root!.Elements("Target").Single();
        Assert.Equal("PodmanHelperMount", target.Attribute("Name")!.Value);
        Assert.Equal("DockerBuildServiceReferences", target.Attribute("AfterTargets")!.Value);
        Assert.Contains("DockerPrepareForLaunch", target.Attribute("BeforeTargets")!.Value);
        Assert.Contains("launch.sem", target.Attribute("Condition")!.Value);
        Assert.Contains("mount --project", target.Element("Exec")!.Attribute("Command")!.Value);
    }

    [Fact]
    public void CommandLineParsing()
    {
        var cli = CommandLine.Parse(new[] { "mount", "--project", @"C:\p", "--timeout=30", "--verbose", "extra" });
        Assert.Equal("mount", cli.Command);
        Assert.Equal(@"C:\p", cli.Get("project"));
        Assert.Equal(30, cli.GetInt("timeout", 0));
        Assert.True(cli.Flag("verbose"));
        Assert.Equal(new[] { "extra" }, cli.Positionals);
    }
}
