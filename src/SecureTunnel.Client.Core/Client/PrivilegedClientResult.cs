using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Core.Client;

/// <summary>
/// The WPF-facing result of a privileged operation, after it has crossed
/// the Named Pipe boundary. Never carries key material. Distinguishes
/// transport-level failure (<see cref="WireGuardOutcome.ServiceUnavailable"/>,
/// <see cref="WireGuardOutcome.PipeConnectionFailed"/>,
/// <see cref="WireGuardOutcome.AccessDenied"/>,
/// <see cref="WireGuardOutcome.InvalidRequest"/>) from WireGuard-level
/// outcomes - callers must not collapse these into one generic failure.
/// <see cref="Configuration"/> is only populated on a successful
/// <see cref="IPrivilegedClientService.ValidateConfigurationAsync"/> call.
/// </summary>
public sealed record PrivilegedClientResult(
    bool Success,
    WireGuardOutcome Outcome,
    string? ErrorCode,
    string? UserSafeMessage,
    ClientState? ObservedState,
    ConfigurationSummary? Configuration = null);
