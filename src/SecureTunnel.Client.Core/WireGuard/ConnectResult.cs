namespace SecureTunnel.Client.Core.WireGuard;

/// <summary>
/// Result of requesting a connection. <see cref="Outcome"/> reflects only
/// what could actually be observed (e.g. the tool started, or failed to
/// start) - it is not itself a claim of an established tunnel. Callers
/// must corroborate with <see cref="IWireGuardClientService.GetStatusAsync"/>
/// before advancing client state to Connected.
/// </summary>
public sealed class ConnectResult
{
    public required WireGuardOutcome Outcome { get; init; }

    public required bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? UserSafeMessage { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
