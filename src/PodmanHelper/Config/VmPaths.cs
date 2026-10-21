namespace PodmanHelper.Config;

/// <summary>Paths inside the Podman machine.</summary>
public static class VmPaths
{
    public const string Root = "/var/lib/podman-helper";
    public const string SyncRoot = Root + "/sync";
    public const string MountsFile = Root + "/mounts.conf";

    /// <summary>
    /// Same translation the Podman Windows client applies to volume sources:
    /// <c>C:\Users\me\vsdbg</c> becomes <c>/mnt/c/Users/me/vsdbg</c>.
    /// </summary>
    public static string FromWindows(string normalizedWindowsPath)
    {
        if (!WindowsPath.IsDriveRooted(normalizedWindowsPath))
        {
            throw new FormatException($"not a drive-rooted Windows path: {normalizedWindowsPath}");
        }
        var drive = char.ToLowerInvariant(normalizedWindowsPath[0]);
        var rest = normalizedWindowsPath[3..].Replace('\\', '/').TrimEnd('/');
        return rest.Length == 0 ? $"/mnt/{drive}" : $"/mnt/{drive}/{rest}";
    }

    /// <summary>Single-quotes a string for a POSIX shell.</summary>
    public static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
