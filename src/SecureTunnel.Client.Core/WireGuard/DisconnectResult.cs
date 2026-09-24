namespace SecureTunnel.Client.Core.WireGuard;

public sealed class DisconnectResult
{
    public required WireGuardOutcome Outcome { get; init; }

    public required bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? UserSafeMessage { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
