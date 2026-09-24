using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.Core.Models;

/// <summary>
/// Metadata describing an imported WireGuard client configuration,
/// including a complete Peer identity. <see cref="PeerPublicKey"/> is not
/// secret (WireGuard public keys are not sensitive) but is still
/// format-validated. <see cref="PresharedKey"/> IS secret and must be
/// treated exactly like a private key - never store it as a plain
/// string, never log it, never place it in an exception message or
/// command-line argument. This type still has no field for the private
/// key itself - that continues to exist only transiently as a
/// <see cref="SecureTunnel.Client.Core.Security.SensitiveString"/>
/// produced by a parser/import result and handed directly to secure
/// storage. Never add a plaintext secret field to this class.
/// </summary>
public sealed class ClientConfiguration
{
    public required string ClientId { get; init; }

    public string? PeerId { get; init; }

    public required string GatewayName { get; init; }

    public required string GatewayEndpoint { get; init; }

    public required string InterfaceAddress { get; init; }

    public IReadOnlyList<string>? DnsServers { get; init; }

    public int? Mtu { get; init; }

    /// <summary>
    /// The WireGuard peer's public key (44-character base64). Mandatory
    /// for a usable client tunnel. Not secret, but format-validated by
    /// the parser like any other WireGuard key.
    /// </summary>
    public required string PeerPublicKey { get; init; }

    /// <summary>
    /// Optional WireGuard preshared key. This IS secret - handled
    /// exactly like the interface private key, never persisted or
    /// logged in plaintext.
    /// </summary>
    public SensitiveString? PresharedKey { get; init; }

    /// <summary>
    /// The WireGuard peer's <c>host:port</c> endpoint - the value that
    /// populates <c>[Peer] Endpoint =</c> in a generated tunnel file.
    /// Distinct from <see cref="GatewayEndpoint"/>, which is product-level
    /// metadata about which Gateway issued this configuration, not
    /// necessarily the literal WireGuard endpoint string.
    /// </summary>
    public required string PeerEndpoint { get; init; }

    /// <summary>
    /// Must always be the explicit set of allowed IP ranges from the
    /// imported configuration. Never silently defaulted or widened
    /// (e.g. to "0.0.0.0/0") by any code in this codebase - doing so
    /// would turn this into an unintended full-tunnel VPN.
    /// </summary>
    public required IReadOnlyList<string> AllowedIPs { get; init; }

    public int? PersistentKeepalive { get; init; }

    public ConfigurationSource ConfigurationSource { get; set; } = ConfigurationSource.Unknown;

    public DateTime ImportedAtUtc { get; set; }

    public DateTime? LastUsedAtUtc { get; set; }

    public ConfigurationStatus Status { get; set; } = ConfigurationStatus.Unverified;
}
