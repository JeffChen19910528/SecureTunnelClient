using System.Text;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.Infrastructure.WireGuard;

/// <summary>
/// Builds a complete, deterministic WireGuard tunnel configuration
/// document ([Interface] + [Peer]) from a <see cref="ClientConfiguration"/>
/// and its secret key material. Field order within each section is fixed
/// so output is reproducible. Never logs its output - the returned string
/// contains the private key (and, if present, the preshared key) in
/// plaintext and must only ever be written to the short-lived, ACL-tight
/// temp file used by <see cref="WireGuardClientService"/>.
/// </summary>
public static class TunnelFileTextBuilder
{
    public static string Build(ClientConfiguration configuration, SensitiveString privateKey, SensitiveString? presharedKey)
    {
        var sb = new StringBuilder();

        sb.Append("[Interface]").Append(Environment.NewLine);
        sb.Append("PrivateKey = ").Append(privateKey.Reveal()).Append(Environment.NewLine);
        sb.Append("Address = ").Append(configuration.InterfaceAddress).Append(Environment.NewLine);

        if (configuration.DnsServers is { Count: > 0 })
        {
            sb.Append("DNS = ").Append(string.Join(", ", configuration.DnsServers)).Append(Environment.NewLine);
        }

        if (configuration.Mtu is { } mtu)
        {
            sb.Append("MTU = ").Append(mtu).Append(Environment.NewLine);
        }

        sb.Append(Environment.NewLine);
        sb.Append("[Peer]").Append(Environment.NewLine);
        sb.Append("PublicKey = ").Append(configuration.PeerPublicKey).Append(Environment.NewLine);

        if (presharedKey is not null)
        {
            sb.Append("PresharedKey = ").Append(presharedKey.Reveal()).Append(Environment.NewLine);
        }

        sb.Append("AllowedIPs = ").Append(string.Join(", ", configuration.AllowedIPs)).Append(Environment.NewLine);
        sb.Append("Endpoint = ").Append(configuration.PeerEndpoint).Append(Environment.NewLine);

        if (configuration.PersistentKeepalive is { } keepalive)
        {
            sb.Append("PersistentKeepalive = ").Append(keepalive).Append(Environment.NewLine);
        }

        return sb.ToString();
    }
}
