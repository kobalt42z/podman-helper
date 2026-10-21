namespace PodmanHelper.Cli;

/// <summary>
/// Minimal argument parser: <c>podman-helper &lt;command&gt; [--key value] [--flag] [positional...]</c>.
/// </summary>
public sealed class CommandLine
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "purge", "verbose", "help", "version", "global-only", "force", "no-start",
    };

    public string? Command { get; private init; }
    public IReadOnlyDictionary<string, string?> Options { get; private init; } = new Dictionary<string, string?>();
    public IReadOnlyList<string> Positionals { get; private init; } = Array.Empty<string>();

    public static CommandLine Parse(string[] args)
    {
        string? command = null;
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var body = arg[2..];
                var eq = body.IndexOf('=');
                if (eq >= 0)
                {
                    options[body[..eq]] = body[(eq + 1)..];
                }
                else if (Flags.Contains(body) || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options[body] = "true";
                }
                else
                {
                    options[body] = args[++i];
                }
            }
            else if (arg is "-h" or "-?" or "/?")
            {
                options["help"] = "true";
            }
            else if (command is null)
            {
                command = arg.ToLowerInvariant();
            }
            else
            {
                positionals.Add(arg);
            }
        }

        return new CommandLine { Command = command, Options = options, Positionals = positionals };
    }

    public bool Has(string name) => Options.ContainsKey(name);

    public bool Flag(string name) =>
        Options.TryGetValue(name, out var v) && (v is null || v.Equals("true", StringComparison.OrdinalIgnoreCase));

    public string? Get(string name) => Options.TryGetValue(name, out var v) ? v : null;

    public int GetInt(string name, int fallback)
    {
        var raw = Get(name);
        return raw is not null && int.TryParse(raw, out var value) ? value : fallback;
    }
}
