using System.IO.Compression;
using System.Reflection;
using PodmanHelper.Cli;

namespace PodmanHelper.Mutagen;

/// <summary>
/// Locates the Mutagen CLI. Preferred: the pinned release bundled as an embedded resource, extracted once to
/// <c>%LOCALAPPDATA%\podman-helper\mutagen\&lt;version&gt;\</c>. Fallback: a <c>mutagen</c> on PATH.
/// </summary>
public sealed class MutagenInstaller
{
    public const string ZipResource = "mutagen.zip";
    public const string LicenseResource = "mutagen.LICENSE";

    public static string Version { get; } =
        typeof(MutagenInstaller).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MutagenVersion")?.Value ?? "unknown";

    public static string RootDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "podman-helper", "mutagen");

    public static string InstallDirectory() => Path.Combine(RootDirectory(), Version);
    public static string DataDirectory() => Path.Combine(RootDirectory(), "data");

    public static string ExePath() => Path.Combine(InstallDirectory(), OperatingSystem.IsWindows() ? "mutagen.exe" : "mutagen");

    public static bool HasBundle() => typeof(MutagenInstaller).Assembly.GetManifestResourceNames().Contains(ZipResource);

    /// <summary>Returns the path of a usable mutagen executable, extracting the bundle on first use.</summary>
    public static string Ensure()
    {
        var exe = ExePath();
        if (File.Exists(exe))
        {
            return exe;
        }

        if (OperatingSystem.IsWindows() && HasBundle())
        {
            Output.Step($"extracting bundled Mutagen v{Version} to {InstallDirectory()}");
            Extract();
            return exe;
        }

        var onPath = FindOnPath(OperatingSystem.IsWindows() ? "mutagen.exe" : "mutagen");
        if (onPath is not null)
        {
            Output.Warn($"using mutagen from PATH ({onPath}); the tool was tested with v{Version}");
            return onPath;
        }

        throw new HelperException(
            "mutagen not found: this build has no bundled Mutagen and none is on PATH. " +
            $"Install v{Version} from https://github.com/mutagen-io/mutagen/releases (or 'winget install Mutagen.Mutagen').");
    }

    public static void Extract()
    {
        var dir = InstallDirectory();
        var temp = dir + ".tmp";
        if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(temp);

        using (var stream = typeof(MutagenInstaller).Assembly.GetManifestResourceStream(ZipResource)
                            ?? throw new HelperException("bundled mutagen.zip resource is missing"))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(temp, entry.FullName));
                if (!target.StartsWith(Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
                {
                    throw new HelperException($"refusing to extract '{entry.FullName}' outside the install directory");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        using (var license = typeof(MutagenInstaller).Assembly.GetManifestResourceStream(LicenseResource))
        {
            if (license is not null)
            {
                using var file = File.Create(Path.Combine(temp, "LICENSE"));
                license.CopyTo(file);
            }
        }

        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.Move(temp, dir);
    }

    public static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // malformed PATH entry
            }
        }
        return null;
    }
}
