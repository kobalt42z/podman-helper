using PodmanHelper.Mutagen;
using Xunit;

namespace PodmanHelper.Tests;

public class MutagenSessionTests
{
    private const string Sample = """
        [
          {
            "identifier": "sync_abc",
            "version": 1,
            "creationTime": "2026-10-21T10:00:00Z",
            "creatingVersion": "0.18.1",
            "alpha": { "protocol": "local", "path": "C:\\Users\\me\\vsdbg", "connected": true, "scanned": true, "files": 120 },
            "beta": { "protocol": "ssh", "host": "podman-helper-podman-machine-default", "path": "/var/lib/podman-helper/sync/abc", "connected": true,
                      "scanProblems": [ { "path": "x", "error": "boom" } ] },
            "name": "ph-abc",
            "labels": { "podman-helper": "1", "ph-scope": "global" },
            "paused": false,
            "status": "watching",
            "successfulCycles": 3,
            "conflicts": [ { "root": "a", "alphaChanges": [], "betaChanges": [] } ]
          },
          {
            "identifier": "sync_def",
            "alpha": { "protocol": "local", "path": "C:\\x", "connected": false },
            "beta": { "protocol": "ssh", "host": "h", "path": "/p", "connected": false },
            "name": "ph-def",
            "paused": true,
            "status": "disconnected",
            "lastError": "ssh failed"
          }
        ]
        """;

    [Fact]
    public void ParsesSessions()
    {
        var sessions = MutagenSession.ParseList(Sample);
        Assert.Equal(2, sessions.Count);

        var a = sessions[0];
        Assert.Equal("ph-abc", a.Name);
        Assert.True(a.IsWatching);
        Assert.Equal(3, a.SuccessfulCycles);
        Assert.Equal(1, a.ConflictCount);
        Assert.Equal(1, a.ProblemCount);
        Assert.False(a.IsSettled);
        Assert.Equal("global", a.Labels["ph-scope"]);
        Assert.Equal("/var/lib/podman-helper/sync/abc", a.BetaPath);
        Assert.Contains("1 conflict", a.Summary);

        var b = sessions[1];
        Assert.True(b.Paused);
        Assert.Equal("paused", b.Summary);
        Assert.Equal("ssh failed", b.LastError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("  \n")]
    public void EmptyListIsEmpty(string json)
    {
        Assert.Empty(MutagenSession.ParseList(json));
    }

    [Fact]
    public void SettledRequiresACompletedCycle()
    {
        var s = MutagenSession.ParseList("""[{"name":"x","alpha":{"path":"a"},"beta":{"path":"b"},"status":"watching","successfulCycles":1}]""").Single();
        Assert.True(s.IsSettled);
        var zero = MutagenSession.ParseList("""[{"name":"x","alpha":{"path":"a"},"beta":{"path":"b"},"status":"watching"}]""").Single();
        Assert.False(zero.IsSettled);
    }
}
