using System.Diagnostics;
using System.Text;
using PodmanHelper.Cli;

namespace PodmanHelper.Processes;

public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Arguments)
{
    public string? StandardInput { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public TimeSpan? Timeout { get; init; }
    /// <summary>Echo stdout/stderr lines to the console as they arrive (for long-running steps).</summary>
    public bool Stream { get; init; }
    public string? WorkingDirectory { get; init; }

    public static ProcessSpec Of(string fileName, params string[] args) => new(fileName, args);

    public string Display => FileName + " " + string.Join(' ', Arguments.Select(a => a.Contains(' ') ? '"' + a + '"' : a));
}

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut)
{
    public bool Success => ExitCode == 0 && !TimedOut;
    public string Combined => (Stdout + "\n" + Stderr).Trim();
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken ct = default)
    {
        Output.Debug(spec.Display);

        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = spec.StandardInput is not null,
            CreateNoWindow = true,
            WorkingDirectory = spec.WorkingDirectory ?? string.Empty,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in spec.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }
        if (spec.Environment is not null)
        {
            foreach (var (key, value) in spec.Environment)
            {
                psi.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            if (spec.Stream) Console.WriteLine("     | " + e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stderr.AppendLine(e.Data);
            if (spec.Stream) Console.WriteLine("     | " + e.Data);
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new HelperException($"cannot start '{spec.FileName}': {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (spec.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(spec.StandardInput);
            process.StandardInput.Close();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (spec.Timeout is { } timeout)
        {
            timeoutCts.CancelAfter(timeout);
        }

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { /* ignore */ }
            if (!timedOut) throw;
        }

        var result = new ProcessResult(timedOut ? -1 : process.ExitCode, stdout.ToString(), stderr.ToString(), timedOut);
        Output.Debug($"exit {result.ExitCode}{(timedOut ? " (timed out)" : "")}");
        return result;
    }
}
