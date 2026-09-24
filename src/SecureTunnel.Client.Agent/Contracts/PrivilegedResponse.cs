using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Agent.Contracts;

/// <summary>
/// Response shape returned for every allow-listed privileged operation.
/// <see cref="UserSafeMessage"/> never includes key material or raw
/// process output. <see cref="Configuration"/> is only populated on a
/// successful <see cref="ValidateConfigurationRequest"/> response.
/// </summary>
public sealed record PrivilegedResponse(
    bool Success,
    WireGuardOutcome Outcome,
    string? ErrorCode,
    string? UserSafeMessage,
    ClientState? ObservedState,
    ConfigurationSummary? Configuration = null);
