using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.Infrastructure.WireGuard.Embedded;

public sealed record NativeTunnelResult(bool Success, string? ErrorCode = null, string? Detail = null);

/// <summary>Point-in-time view of an adapter. <see cref="LastHandshakeUtc"/> is null until a handshake completed.</summary>
public sealed record NativeTunnelSnapshot(DateTime? LastHandshakeUtc, ulong RxBytes, ulong TxBytes);

/// <summary>
/// In-process WireGuard data plane (wireguard-nt). Exists so the connection logic in
/// <see cref="EmbeddedWireGuardClientService"/> can be unit-tested without a driver or elevation.
/// </summary>
public interface IWireGuardNativeTunnel
{
    /// <summary>False (with a reason) when the bundled driver library cannot be loaded.</summary>
    bool IsAvailable(out string? reason);

    NativeTunnelResult Up(string adapterName, ClientConfiguration configuration, SensitiveString privateKey, SensitiveString? presharedKey);

    NativeTunnelResult Down(string adapterName);

    /// <summary>Null when no adapter with this name is held by this process.</summary>
    NativeTunnelSnapshot? Query(string adapterName);
}
