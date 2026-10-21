using System.Reflection;
using PodmanHelper.Cli;
using PodmanHelper.Config;

namespace PodmanHelper.Commands;

/// <summary>
/// Creates the global list if missing, the project list next to the .dcproj, the podman-helper.targets file,
/// and the one import line in the .dcproj. Everything is idempotent and never overwrites an existing file
/// unless <c>--force</c> is given.
/// </summary>
public static class InitCommand
{
    public const string ImportLine = "<Import Project=\"podman-helper.targets\" Condition=\"Exists('podman-helper.targets')\" />";

    public static Task<int> RunAsync(CommandLine cli, CancellationToken ct)
    {
        var force = cli.Flag("force");

        var globalFile = Workspace.DefaultGlobalFile();
        WriteIfMissing(globalFile, Resource("filemap-list.global.txt"), force);

        if (cli.Flag("global-only"))
        {
            return Task.FromResult(0);
        }

        var projectDir = Path.GetFullPath(cli.Get("project") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(projectDir))
        {
            throw new HelperException($"project directory does not exist: {projectDir}", 2);
        }

        var dcprojs = Directory.GetFiles(projectDir, "*.dcproj");
        if (dcprojs.Length == 0)
        {
            Output.Warn($"no .dcproj in {projectDir}; writing {Workspace.FileName} there anyway (use --project <dir with the .dcproj> for the Visual Studio hook)");
        }

        WriteIfMissing(Path.Combine(projectDir, Workspace.FileName), Resource("filemap-list.project.txt"), force);

        if (dcprojs.Length > 0)
        {
            var targets = Path.Combine(projectDir, Workspace.TargetsFileName);
            var content = Resource("podman-helper.targets");
            if (!File.Exists(targets) || force || File.ReadAllText(targets) != content)
            {
                File.WriteAllText(targets, content);
                Output.Ok($"wrote {targets}");
            }
            else
            {
                Output.Info($"up to date: {targets}");
            }

            foreach (var dcproj in dcprojs)
            {
                var text = File.ReadAllText(dcproj);
                var updated = AddImport(text);
                if (updated is null)
                {
                    Output.Info($"import already present: {dcproj}");
                }
                else
                {
                    File.WriteAllText(dcproj, updated);
                    Output.Ok($"added import to {dcproj}");
                }
            }
        }

        Output.Info("next: edit the filemap-list.txt files, then run 'podman-helper doctor' and 'podman-helper mount'");
        return Task.FromResult(0);
    }

    /// <summary>Inserts the import before the closing &lt;/Project&gt; tag; returns null when it is already there.</summary>
    internal static string? AddImport(string dcproj)
    {
        if (dcproj.Contains("podman-helper.targets", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var close = dcproj.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            throw new HelperException("the .dcproj has no </Project> closing tag");
        }
        var newline = dcproj.Contains("\r\n") ? "\r\n" : "\n";
        var before = dcproj[..close].TrimEnd();
        return before + newline + newline + "  " + ImportLine + newline + dcproj[close..];
    }

    private static void WriteIfMissing(string path, string content, bool force)
    {
        if (File.Exists(path) && !force)
        {
            Output.Info($"exists: {path}");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Output.Ok($"wrote {path}");
    }

    internal static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new HelperException($"embedded resource missing: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
