using System.Text;

namespace PodmanHelper.Ssh;

/// <summary>
/// Keeps one managed <c>Host</c> block per podman machine in <c>~/.ssh/config</c>, so that both
/// <c>ssh.exe</c> and Mutagen (which uses the system OpenSSH client) share the identity, port and user.
/// </summary>
public static class SshConfigManager
{
    public const string AliasPrefix = "podman-helper-";

    public static string AliasFor(string machineName) => AliasPrefix + machineName;

    /// <summary>Private known_hosts for the machine's host key, which changes on every <c>podman machine init</c>.</summary>
    public static string KnownHostsPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "podman-helper", "known_hosts");

    public static string DefaultConfigPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    public static string BuildBlock(string alias, string host, int port, string user, string identityFile)
    {
        var sb = new StringBuilder();
        sb.Append("# >>> ").Append(alias).Append(" (managed by podman-helper, do not edit) >>>\n");
        sb.Append("Host ").Append(alias).Append('\n');
        sb.Append("  HostName ").Append(host).Append('\n');
        sb.Append("  Port ").Append(port).Append('\n');
        sb.Append("  User ").Append(user).Append('\n');
        sb.Append("  IdentityFile \"").Append(identityFile).Append("\"\n");
        sb.Append("  IdentitiesOnly yes\n");
        sb.Append("  StrictHostKeyChecking no\n");
        // A real file, not NUL: an ssh that does not treat NUL as the null device (Git for Windows, MSYS)
        // would create a file literally named "NUL" in the current folder, e.g. the .dcproj folder on F5.
        sb.Append("  UserKnownHostsFile \"").Append(KnownHostsPath()).Append("\"\n");
        sb.Append("  LogLevel ERROR\n");
        sb.Append("  ServerAliveInterval 30\n");
        sb.Append("# <<< ").Append(alias).Append(" <<<\n");
        return sb.ToString();
    }

    /// <summary>Returns the new file content with the block for <paramref name="alias"/> inserted or replaced.</summary>
    public static string Upsert(string? existing, string alias, string block)
    {
        var startMarker = $"# >>> {alias} ";
        var endMarker = $"# <<< {alias} <<<";
        var text = (existing ?? string.Empty).Replace("\r\n", "\n");

        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start >= 0)
        {
            var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
            if (end >= 0)
            {
                end += endMarker.Length;
                if (end < text.Length && text[end] == '\n') end++;
                text = text[..start] + block + text[end..];
                return text;
            }
        }

        // OpenSSH uses the first value it finds for each option, so the block goes first: a "Host *"
        // block above it would otherwise win for User and IdentityFile.
        return text.Length == 0 ? block : block + "\n" + text;
    }

    /// <summary>Returns true when the file content already contains exactly this block.</summary>
    public static bool Contains(string? existing, string block) =>
        existing is not null && existing.Replace("\r\n", "\n").Contains(block, StringComparison.Ordinal);

    public static bool EnsureAlias(string configPath, string alias, string host, int port, string user, string identityFile)
    {
        var block = BuildBlock(alias, host, port, user, identityFile);
        var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        if (Contains(existing, block))
        {
            return false;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(KnownHostsPath())!);
        File.WriteAllText(configPath, Upsert(existing, alias, block));
        return true;
    }
}
