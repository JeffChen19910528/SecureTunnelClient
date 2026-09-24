using SecureTunnel.Client.Core.Models;

namespace SecureTunnel.Client.Core.WireGuard;

/// <summary>
/// The authoritative source for confirming an active tunnel. This is the
/// only result type whose <see cref="ObservedState"/> may legitimately
/// justify a transition to <see cref="ClientState.Connected"/>.
/// </summary>
public sealed class StatusResult
{
    public required WireGuardOutcome Outcome { get; init; }

    public required bool Success { get; init; }

    public required ClientState ObservedState { get; init; }

    public string? ErrorCode { get; init; }

    public string? UserSafeMessage { get; init; }

    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
