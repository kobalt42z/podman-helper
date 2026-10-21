using System.Text;

namespace PodmanHelper.Config;

public sealed class FileMapParseResult
{
    public List<FileMapEntry> Entries { get; } = new();
    /// <summary>Normalised paths removed from the global list by <c>!path</c> lines.</summary>
    public List<string> Exclusions { get; } = new();
    public List<string> Errors { get; } = new();
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>Parses the <c>filemap-list.txt</c> format. See the templates under Assets for the syntax.</summary>
public static class FileMapParser
{
    private static readonly string[] KnownKeys = { "mode", "x", "ignore", "target", "wait" };

    public static FileMapParseResult Parse(string text, string baseDirectory, string sourceFile, string scope, Func<string, string?> getEnv)
    {
        var result = new FileMapParseResult();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var line = StripComment(lines[i]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            List<string> tokens;
            try
            {
                tokens = Tokenize(line);
            }
            catch (FormatException ex)
            {
                result.Errors.Add($"{sourceFile}:{lineNo}: {ex.Message}");
                continue;
            }

            if (tokens.Count == 0)
            {
                continue;
            }

            var rawPath = tokens[0];
            var exclusion = false;
            if (rawPath.StartsWith('!'))
            {
                exclusion = true;
                rawPath = rawPath[1..];
                if (rawPath.Length == 0)
                {
                    result.Errors.Add($"{sourceFile}:{lineNo}: '!' must be followed by a path");
                    continue;
                }
            }

            string normalized;
            try
            {
                normalized = WindowsPath.Normalize(rawPath, baseDirectory, getEnv);
            }
            catch (FormatException ex)
            {
                result.Errors.Add($"{sourceFile}:{lineNo}: {ex.Message}");
                continue;
            }

            if (exclusion)
            {
                if (tokens.Count > 1)
                {
                    result.Errors.Add($"{sourceFile}:{lineNo}: options are not allowed on a '!' exclusion line");
                    continue;
                }
                result.Exclusions.Add(normalized);
                continue;
            }

            var mode = SyncMode.OneWayReplica;
            var exec = new List<string>();
            var ignores = new List<string>();
            string? target = null;
            var wait = true;
            var ok = true;

            foreach (var token in tokens.Skip(1))
            {
                var eq = token.IndexOf('=');
                if (eq <= 0)
                {
                    result.Errors.Add($"{sourceFile}:{lineNo}: expected key=value, got '{token}'");
                    ok = false;
                    break;
                }
                var key = token[..eq].ToLowerInvariant();
                var value = token[(eq + 1)..];
                switch (key)
                {
                    case "mode":
                        if (!SyncModes.TryParse(value, out mode))
                        {
                            result.Errors.Add($"{sourceFile}:{lineNo}: unknown mode '{value}' (one-way-replica, one-way-safe, two-way-safe, two-way-resolved)");
                            ok = false;
                        }
                        break;
                    case "x":
                        exec.AddRange(SplitList(value));
                        break;
                    case "ignore":
                        ignores.AddRange(SplitList(value));
                        break;
                    case "target":
                        if (!value.StartsWith('/'))
                        {
                            result.Errors.Add($"{sourceFile}:{lineNo}: target must be an absolute VM path, got '{value}'");
                            ok = false;
                        }
                        target = value.TrimEnd('/');
                        break;
                    case "wait":
                        if (!bool.TryParse(value, out wait))
                        {
                            result.Errors.Add($"{sourceFile}:{lineNo}: wait must be true or false, got '{value}'");
                            ok = false;
                        }
                        break;
                    default:
                        result.Errors.Add($"{sourceFile}:{lineNo}: unknown option '{key}' (known: {string.Join(", ", KnownKeys)})");
                        ok = false;
                        break;
                }
                if (!ok)
                {
                    break;
                }
            }

            if (!ok)
            {
                continue;
            }

            result.Entries.Add(new FileMapEntry
            {
                WindowsPath = normalized,
                Mode = mode,
                ExecGlobs = exec,
                Ignores = ignores,
                TargetOverride = target,
                Wait = wait,
                Scope = scope,
                SourceFile = sourceFile,
                Line = lineNo,
            });
        }

        return result;
    }

    private static IEnumerable<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A '#' starts a comment at the beginning of a line or after whitespace.</summary>
    private static string StripComment(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == '#' && !inQuotes && (i == 0 || char.IsWhiteSpace(line[i - 1])))
            {
                return line[..i];
            }
        }
        return line;
    }

    /// <summary>Splits on whitespace; double quotes group a token (so paths with spaces work).</summary>
    internal static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (inQuotes)
        {
            throw new FormatException("unbalanced double quote");
        }
        if (hasToken)
        {
            tokens.Add(current.ToString());
        }
        return tokens;
    }
}
