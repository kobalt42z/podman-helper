using PodmanHelper.Cli;

namespace PodmanHelper.Config;

/// <summary>Locates and loads the global and the per-project filemap lists.</summary>
public sealed class Workspace
{
    public const string FileName = "filemap-list.txt";
    public const string TargetsFileName = "podman-helper.targets";

    public required string GlobalFile { get; init; }
    public string? ProjectDirectory { get; init; }
    public string? ProjectFile { get; init; }
    public required MergeResult Merged { get; init; }

    public IReadOnlyList<FileMapEntry> Entries => Merged.Entries;
    public string ProjectScope => ProjectDirectory is null ? FileMapEntry.GlobalScope : WindowsPath.Key(ProjectDirectory);

    public static string DefaultGlobalFile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".podman-helper", FileName);
    }

    public static string DefaultGlobalDirectory() => Path.GetDirectoryName(DefaultGlobalFile())!;

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> until it finds a filemap-list.txt or a .dcproj.
    /// Returns null when nothing is found.
    /// </summary>
    public static string? FindProjectDirectory(string startDirectory)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, FileName)) || dir.EnumerateFiles("*.dcproj").Any())
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    public static Workspace Load(string? projectOption, bool requireProject)
    {
        var globalFile = DefaultGlobalFile();
        var getEnv = (Func<string, string?>)Environment.GetEnvironmentVariable;

        FileMapParseResult global;
        if (File.Exists(globalFile))
        {
            global = FileMapParser.Parse(File.ReadAllText(globalFile), Path.GetDirectoryName(globalFile)!, globalFile, FileMapEntry.GlobalScope, getEnv);
        }
        else
        {
            global = new FileMapParseResult();
            Output.Warn($"no global file at {globalFile} (run 'podman-helper init' to create one)");
        }

        string? projectDir = null;
        string? projectFile = null;
        FileMapParseResult? project = null;

        var start = projectOption ?? Directory.GetCurrentDirectory();
        if (projectOption is not null && !Directory.Exists(projectOption))
        {
            throw new HelperException($"project directory does not exist: {projectOption}", 2);
        }

        projectDir = FindProjectDirectory(start);
        if (projectDir is not null)
        {
            projectFile = Path.Combine(projectDir, FileName);
            if (File.Exists(projectFile))
            {
                var scope = WindowsPath.Key(ToWindowsForm(projectDir));
                project = FileMapParser.Parse(File.ReadAllText(projectFile), ToWindowsForm(projectDir), projectFile, scope, getEnv);
            }
            else
            {
                Output.Warn($"no {FileName} in {projectDir} (run 'podman-helper init' there to create one); using global entries only");
                projectFile = null;
            }
        }
        else if (requireProject)
        {
            Output.Warn($"no {FileName} or .dcproj found above {start}; using global entries only");
        }

        var merged = FileMapMerger.Merge(global, project);
        return new Workspace
        {
            GlobalFile = globalFile,
            ProjectDirectory = projectDir,
            ProjectFile = projectFile,
            Merged = merged,
        };
    }

    /// <summary>Entries that belong to this project (not global).</summary>
    public IEnumerable<FileMapEntry> ProjectEntries => Entries.Where(e => !e.IsGlobal);

    /// <summary>On Linux dev machines a path like /home/x is not a Windows path; the parser needs X:\ form.</summary>
    private static string ToWindowsForm(string path) =>
        WindowsPath.IsDriveRooted(path) ? path : Path.GetFullPath(path);
}
