namespace SecureTunnel.Client.Core.Client.Ipc;

/// <summary>
/// Wire-level response envelope. Enums are serialized as strings for
/// forward compatibility. Never contains a private key, preshared key, or
/// any other secret - only status/outcome metadata. The <c>Config*</c>
/// fields are a flattened, optional projection of
/// <see cref="SecureTunnel.Client.Core.Configuration.ConfigurationSummary"/>
/// (only populated on a successful ValidateConfiguration response) - kept
/// as flat nullable primitives rather than a nested object to keep the
/// wire schema simple; none of them can ever carry key material because
/// <c>ConfigurationSummary</c> itself has no key-typed property.
/// </summary>
public sealed record PipeResponseEnvelope(
    bool Success,
    string Outcome,
    string? ErrorCode,
    string? UserSafeMessage,
    string? ObservedState,
    string? ConfigClientId = null,
    string? ConfigGatewayEndpoint = null,
    string? ConfigInterfaceAddress = null,
    IReadOnlyList<string>? ConfigAllowedIPs = null);
