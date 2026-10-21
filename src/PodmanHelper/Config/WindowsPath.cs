using System.Text;

namespace PodmanHelper.Config;

/// <summary>
/// Windows path normalisation that does not depend on the host OS, so the parser behaves the same
/// in tests on Linux and in production on Windows.
/// </summary>
public static class WindowsPath
{
    public static bool IsDriveRooted(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    public static bool IsUnc(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    /// <summary>
    /// Expands <c>~</c> and <c>%VAR%</c>, resolves relative paths against <paramref name="baseDirectory"/>,
    /// collapses <c>.</c> and <c>..</c>, and returns a <c>X:\...</c> path with no trailing separator.
    /// </summary>
    public static string Normalize(string raw, string baseDirectory, Func<string, string?> getEnv)
    {
        var path = ExpandEnvironment(raw.Trim(), getEnv);

        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
        {
            var home = getEnv("USERPROFILE") ?? getEnv("HOME")
                ?? throw new FormatException("cannot expand '~': neither USERPROFILE nor HOME is set");
            path = home + path[1..];
        }

        path = path.Replace('/', '\\');

        if (IsUnc(path))
        {
            throw new FormatException($"UNC paths are not supported: {raw}");
        }

        if (!IsDriveRooted(path))
        {
            if (path.Length >= 2 && path[1] == ':')
            {
                throw new FormatException($"drive-relative paths are not supported: {raw}");
            }
            var baseNorm = baseDirectory.Replace('/', '\\').TrimEnd('\\');
            if (!IsDriveRooted(baseNorm + "\\"))
            {
                throw new FormatException($"base directory is not a rooted Windows path: {baseDirectory}");
            }
            path = baseNorm + "\\" + path;
        }

        var drive = char.ToUpperInvariant(path[0]);
        var segments = new List<string>();
        foreach (var segment in path[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
                continue;
            }
            segments.Add(segment);
        }

        var sb = new StringBuilder();
        sb.Append(drive).Append(":\\");
        sb.Append(string.Join('\\', segments));
        return sb.ToString();
    }

    /// <summary>Case-insensitive key for comparing Windows paths.</summary>
    public static string Key(string normalized) => normalized.ToLowerInvariant();

    /// <summary>True when <paramref name="child"/> is inside <paramref name="parent"/> (both normalised).</summary>
    public static bool IsInside(string parent, string child)
    {
        var p = Key(parent).TrimEnd('\\') + "\\";
        var c = Key(child).TrimEnd('\\') + "\\";
        return c.Length > p.Length && c.StartsWith(p, StringComparison.Ordinal);
    }

    private static string ExpandEnvironment(string input, Func<string, string?> getEnv)
    {
        if (!input.Contains('%'))
        {
            return input;
        }

        var sb = new StringBuilder();
        var i = 0;
        while (i < input.Length)
        {
            if (input[i] == '%')
            {
                var end = input.IndexOf('%', i + 1);
                if (end > i + 1)
                {
                    var name = input[(i + 1)..end];
                    var value = getEnv(name);
                    if (value is not null)
                    {
                        sb.Append(value);
                        i = end + 1;
                        continue;
                    }
                }
            }
            sb.Append(input[i]);
            i++;
        }
        return sb.ToString();
    }
}
