using SecureTunnel.Client.Infrastructure.WireGuard;

namespace SecureTunnel.Client.TestSupport;

/// <summary>
/// Test double - not a real implementation. Never launches a real process;
/// records every call so tests can assert no secret material was ever
/// passed as an argument. Set <see cref="ThrowOperationCanceled"/> to
/// simulate an operation-timeout scenario (mirrors what the real
/// <c>WireGuardProcessRunner</c> does on cancellation).
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public bool ExecutableAvailable { get; set; } = true;
    public ProcessRunResult NextResult { get; set; } = new(Started: true, ExitCode: 0, StandardOutput: string.Empty, StandardError: string.Empty);
    public bool ThrowOperationCanceled { get; set; }
    public bool KillRequested { get; private set; }

    /// <summary>
    /// Optional per-call override, keyed by the first argument (the
    /// WireGuard sub-command, e.g. "/dumptunnelservice"), so a single fake
    /// can return different results for different calls within one test
    /// (e.g. a status check followed by an uninstall attempt).
    /// </summary>
    public Dictionary<string, ProcessRunResult> ResultsByFirstArgument { get; } = [];

    public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments));

        if (ThrowOperationCanceled)
        {
            KillRequested = true;
            throw new OperationCanceledException(cancellationToken);
        }

        if (arguments.Count > 0 && ResultsByFirstArgument.TryGetValue(arguments[0], out var overrideResult))
        {
            return Task.FromResult(overrideResult);
        }

        return Task.FromResult(NextResult);
    }

    public bool IsExecutableAvailable(string fileName) => ExecutableAvailable;
}
