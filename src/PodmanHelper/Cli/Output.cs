namespace PodmanHelper.Cli;

/// <summary>Console output with uniform prefixes; MSBuild shows all of it in the Build Output window.</summary>
public static class Output
{
    public static bool Verbose { get; set; }

    public static void Step(string message) => Console.WriteLine($"[..] {message}");
    public static void Ok(string message) => Console.WriteLine($"[ok] {message}");
    public static void Warn(string message) => Console.WriteLine($"[!!] {message}");
    public static void Info(string message) => Console.WriteLine($"     {message}");
    public static void Error(string message) => Console.Error.WriteLine($"podman-helper: error: {message}");

    public static void Debug(string message)
    {
        if (Verbose)
        {
            Console.WriteLine($"     > {message}");
        }
    }
}
