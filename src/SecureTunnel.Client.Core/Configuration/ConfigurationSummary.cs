namespace SecureTunnel.Client.Core.Configuration;

/// <summary>
/// A non-secret, UI-safe projection of an imported/validated configuration.
/// Deliberately has no key-typed property - it is structurally impossible
/// to leak a private or preshared key through this type.
/// </summary>
public sealed record ConfigurationSummary(
    string ClientId,
    string GatewayEndpoint,
    string InterfaceAddress,
    IReadOnlyList<string> AllowedIPs);
