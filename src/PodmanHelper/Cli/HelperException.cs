namespace PodmanHelper.Cli;

/// <summary>An error with a message meant for the user; the CLI prints it and exits with <see cref="ExitCode"/>.</summary>
public sealed class HelperException : Exception
{
    public int ExitCode { get; }

    public HelperException(string message, int exitCode = 1) : base(message)
    {
        ExitCode = exitCode;
    }
}
