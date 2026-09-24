namespace SecureTunnel.Client.Infrastructure.WireGuard;

/// <summary>
/// Isolates external process execution so <see cref="WireGuardClientService"/>
/// can be unit tested without shelling out to a real binary. Implementations
/// must launch processes using an argument list (never a concatenated
/// command-line string) and must never receive secret material as one of
/// <paramref name="arguments"/>.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    bool IsExecutableAvailable(string fileName);
}
