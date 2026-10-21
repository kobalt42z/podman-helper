namespace PodmanHelper.Config;

public sealed class MergeResult
{
    public List<FileMapEntry> Entries { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Union of the global and the project lists. A project entry with the same path replaces the global
/// one; a project <c>!path</c> removes the global entry; overlapping entries are an error because Mutagen
/// forbids nested sync roots and the inner bind mount would be hidden by the outer one.
/// </summary>
public static class FileMapMerger
{
    public static MergeResult Merge(FileMapParseResult global, FileMapParseResult? project)
    {
        var result = new MergeResult();
        result.Errors.AddRange(global.Errors);
        if (project is not null)
        {
            result.Errors.AddRange(project.Errors);
        }

        var byKey = new Dictionary<string, FileMapEntry>(StringComparer.Ordinal);
        var order = new List<string>();

        void Add(FileMapEntry entry)
        {
            if (byKey.TryGetValue(entry.Key, out var existing))
            {
                if (existing.Scope == entry.Scope)
                {
                    result.Errors.Add($"{entry.SourceFile}:{entry.Line}: duplicate entry for {entry.WindowsPath} (first at line {existing.Line})");
                    return;
                }
                result.Warnings.Add($"{entry.SourceFile}:{entry.Line}: overrides the global entry for {entry.WindowsPath}");
                byKey[entry.Key] = entry;
                return;
            }
            byKey[entry.Key] = entry;
            order.Add(entry.Key);
        }

        foreach (var entry in global.Entries)
        {
            Add(entry);
        }

        if (project is not null)
        {
            foreach (var exclusion in project.Exclusions)
            {
                var key = WindowsPath.Key(exclusion);
                if (byKey.TryGetValue(key, out var removed) && removed.IsGlobal)
                {
                    byKey.Remove(key);
                    order.Remove(key);
                }
                else
                {
                    result.Warnings.Add($"'!{exclusion}' does not match any global entry");
                }
            }

            foreach (var entry in project.Entries)
            {
                Add(entry);
            }
        }

        foreach (var key in order)
        {
            result.Entries.Add(byKey[key]);
        }

        for (var i = 0; i < result.Entries.Count; i++)
        {
            for (var j = 0; j < result.Entries.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }
                var outer = result.Entries[i];
                var inner = result.Entries[j];
                if (WindowsPath.IsInside(outer.WindowsPath, inner.WindowsPath))
                {
                    result.Errors.Add($"{inner.SourceFile}:{inner.Line}: {inner.WindowsPath} is inside {outer.WindowsPath} ({outer.SourceFile}:{outer.Line}); nested entries are not allowed");
                }
            }
        }

        var targets = new Dictionary<string, FileMapEntry>(StringComparer.Ordinal);
        foreach (var entry in result.Entries)
        {
            if (targets.TryGetValue(entry.VmTarget, out var other))
            {
                result.Errors.Add($"{entry.SourceFile}:{entry.Line}: VM target {entry.VmTarget} is also used by {other.WindowsPath}");
            }
            else
            {
                targets[entry.VmTarget] = entry;
            }
        }

        return result;
    }
}
