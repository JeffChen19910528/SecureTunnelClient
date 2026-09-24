using System.ComponentModel;
using System.Diagnostics;

namespace SecureTunnel.Client.Infrastructure.WireGuard;

/// <summary>
/// Real <see cref="IProcessRunner"/> implementation. Always uses
/// <see cref="ProcessStartInfo.ArgumentList"/> - never builds a
/// shell-concatenated command-line string via
/// <see cref="ProcessStartInfo.Arguments"/>, and never invokes
/// <c>cmd.exe</c>/<c>powershell.exe</c> as an intermediary. On
/// cancellation (e.g. an operation timeout), kills the process tree
/// rather than leaving an orphaned process behind.
/// </summary>
public sealed class WireGuardProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        bool started;
        try
        {
            started = process.Start();
        }
        catch (Win32Exception)
        {
            // The executable could not be launched at all (missing,
            // invalid path, access denied at the OS level).
            return new ProcessRunResult(Started: false, ExitCode: -1, StandardOutput: string.Empty, StandardError: string.Empty);
        }

        if (!started)
        {
            return new ProcessRunResult(Started: false, ExitCode: -1, StandardOutput: string.Empty, StandardError: string.Empty);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ProcessRunResult(Started: true, ExitCode: process.ExitCode, StandardOutput: await stdOutTask, StandardError: await stdErrTask);
    }

    public bool IsExecutableAvailable(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var directory in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return File.Exists(fileName);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill call.
        }
    }
}
