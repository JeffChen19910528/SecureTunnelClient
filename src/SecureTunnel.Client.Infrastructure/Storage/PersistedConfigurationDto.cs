namespace SecureTunnel.Client.Infrastructure.Storage;

/// <summary>
/// Plaintext shape serialized before DPAPI encryption. This type only ever
/// exists in memory and inside the encrypted-at-rest blob - it must never
/// be logged, and no code path may write an instance of this type to disk
/// without going through <see cref="DpapiClientConfigurationStore"/>'s
/// encryption step. <see cref="PrivateKey"/> and <see cref="PresharedKey"/>
/// are the only secret fields; everything else is non-secret configuration
/// metadata that happens to be encrypted alongside them for simplicity.
/// </summary>
internal sealed class PersistedConfigurationDto
{
    public required string ClientId { get; init; }
    public string? PeerId { get; init; }
    public required string GatewayName { get; init; }
    public required string GatewayEndpoint { get; init; }
    public required string InterfaceAddress { get; init; }
    public List<string>? DnsServers { get; init; }
    public int? Mtu { get; init; }
    public required string PeerPublicKey { get; init; }
    public required string PeerEndpoint { get; init; }
    public required List<string> AllowedIPs { get; init; }
    public int? PersistentKeepalive { get; init; }
    public required string ConfigurationSource { get; init; }
    public required DateTime ImportedAtUtc { get; init; }
    public DateTime? LastUsedAtUtc { get; init; }
    public required string Status { get; init; }
    public required string PrivateKey { get; init; }
    public string? PresharedKey { get; init; }
}
