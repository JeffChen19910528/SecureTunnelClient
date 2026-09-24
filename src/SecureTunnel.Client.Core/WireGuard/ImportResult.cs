namespace SecureTunnel.Client.Core.WireGuard;

public sealed class ImportResult
{
    public required WireGuardOutcome Outcome { get; init; }

    public required bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? UserSafeMessage { get; init; }
}
