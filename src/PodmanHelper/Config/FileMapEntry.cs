using System.Security.Cryptography;
using System.Text;

namespace PodmanHelper.Config;

public enum SyncMode
{
    OneWayReplica,
    OneWaySafe,
    TwoWaySafe,
    TwoWayResolved,
}

public static class SyncModes
{
    public static string ToMutagen(this SyncMode mode) => mode switch
    {
        SyncMode.OneWayReplica => "one-way-replica",
        SyncMode.OneWaySafe => "one-way-safe",
        SyncMode.TwoWaySafe => "two-way-safe",
        SyncMode.TwoWayResolved => "two-way-resolved",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public static bool TryParse(string text, out SyncMode mode)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "one-way-replica": mode = SyncMode.OneWayReplica; return true;
            case "one-way-safe": mode = SyncMode.OneWaySafe; return true;
            case "two-way-safe": mode = SyncMode.TwoWaySafe; return true;
            case "two-way-resolved": mode = SyncMode.TwoWayResolved; return true;
            default: mode = default; return false;
        }
    }
}

/// <summary>One line of a filemap-list.txt, fully resolved.</summary>
public sealed record FileMapEntry
{
    public const string GlobalScope = "global";

    /// <summary>Normalised absolute Windows path (X:\...).</summary>
    public required string WindowsPath { get; init; }
    public SyncMode Mode { get; init; } = SyncMode.OneWayReplica;
    public IReadOnlyList<string> ExecGlobs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Ignores { get; init; } = Array.Empty<string>();
    public string? TargetOverride { get; init; }
    public bool Wait { get; init; } = true;

    /// <summary><see cref="GlobalScope"/> or the normalised directory of the project file.</summary>
    public required string Scope { get; init; }
    public required string SourceFile { get; init; }
    public required int Line { get; init; }

    public bool IsGlobal => Scope == GlobalScope;
    public string Key => Config.WindowsPath.Key(WindowsPath);

    /// <summary>Stable short id derived from the path; used for the Mutagen session name and the VM directory.</summary>
    public string Id => EntryId.For(WindowsPath);
    public string SessionName => "ph-" + Id;
    public string VmSyncDirectory => VmPaths.SyncRoot + "/" + Id;
    public string VmTarget => TargetOverride ?? VmPaths.FromWindows(WindowsPath);
}

public static class EntryId
{
    public static string For(string normalizedWindowsPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(WindowsPath.Key(normalizedWindowsPath)));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}

public static class ScopeId
{
    public static string For(string scope) => scope == FileMapEntry.GlobalScope ? scope : EntryId.For(scope);
}
