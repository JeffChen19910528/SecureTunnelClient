using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SecureTunnel.Client.Infrastructure.WireGuard.Embedded;

/// <summary>
/// Serialises a configuration into the WIREGUARD_INTERFACE / WIREGUARD_PEER / WIREGUARD_ALLOWED_IP
/// byte layout from wireguard-nt's wireguard.h (all structs 8-byte aligned). Offsets are written
/// explicitly rather than via marshalled structs so the layout is visible and unit-testable.
/// </summary>
public static class WireGuardNtConfigBuilder
{
    public const int KeyLength = 32;
    public const int InterfaceSize = 80;
    public const int PeerSize = 136;
    public const int AllowedIpSize = 24;
    public const int SockaddrInetSize = 28;

    private const uint InterfaceHasPrivateKey = 1 << 1;
    private const uint InterfaceReplacePeers = 1 << 3;
    private const uint PeerHasPublicKey = 1 << 0;
    private const uint PeerHasPresharedKey = 1 << 1;
    private const uint PeerHasKeepalive = 1 << 2;
    private const uint PeerHasEndpoint = 1 << 3;
    private const uint PeerReplaceAllowedIps = 1 << 5;

    public static byte[] Build(
        byte[] privateKey,
        byte[] peerPublicKey,
        byte[]? presharedKey,
        IPEndPoint endpoint,
        ushort keepaliveSeconds,
        IReadOnlyList<IPNetwork> allowedIps)
    {
        if (privateKey.Length != KeyLength || peerPublicKey.Length != KeyLength || presharedKey is { Length: not KeyLength })
        {
            throw new ArgumentException("WireGuard keys must be exactly 32 bytes.");
        }

        var buffer = new byte[InterfaceSize + PeerSize + allowedIps.Count * AllowedIpSize];
        var span = buffer.AsSpan();

        // WIREGUARD_INTERFACE: Flags@0, ListenPort@4, PrivateKey@6, PublicKey@38, PeersCount@72
        BitConverter.TryWriteBytes(span[0..], InterfaceHasPrivateKey | InterfaceReplacePeers);
        privateKey.CopyTo(span[6..]);
        BitConverter.TryWriteBytes(span[72..], 1u);

        // WIREGUARD_PEER at 80: Flags@0, PublicKey@8, PresharedKey@40, Keepalive@72, Endpoint@76, AllowedIPsCount@128
        var peer = span[InterfaceSize..];
        var flags = PeerHasPublicKey | PeerHasEndpoint | PeerReplaceAllowedIps;
        if (presharedKey is not null) flags |= PeerHasPresharedKey;
        if (keepaliveSeconds > 0) flags |= PeerHasKeepalive;
        BitConverter.TryWriteBytes(peer[0..], flags);
        peerPublicKey.CopyTo(peer[8..]);
        presharedKey?.CopyTo(peer[40..]);
        BitConverter.TryWriteBytes(peer[72..], keepaliveSeconds);
        WriteSockaddr(peer.Slice(76, SockaddrInetSize), endpoint);
        BitConverter.TryWriteBytes(peer[128..], (uint)allowedIps.Count);

        // WIREGUARD_ALLOWED_IP[n]: Address@0 (16), AddressFamily@16, Cidr@18
        var ips = peer[PeerSize..];
        for (var i = 0; i < allowedIps.Count; i++)
        {
            var entry = ips.Slice(i * AllowedIpSize, AllowedIpSize);
            var net = allowedIps[i];
            net.BaseAddress.GetAddressBytes().CopyTo(entry);
            BitConverter.TryWriteBytes(entry[16..], (ushort)(net.BaseAddress.AddressFamily == AddressFamily.InterNetwork ? 2 : 23));
            entry[18] = (byte)net.PrefixLength;
        }

        return buffer;
    }

    internal static void WriteSockaddr(Span<byte> target, IPEndPoint endpoint)
    {
        var addr = endpoint.Address.GetAddressBytes();
        var isV4 = endpoint.AddressFamily == AddressFamily.InterNetwork;
        BitConverter.TryWriteBytes(target[0..], (ushort)(isV4 ? 2 : 23));
        target[2] = (byte)(endpoint.Port >> 8); // port is network byte order
        target[3] = (byte)endpoint.Port;
        if (isV4)
        {
            addr.CopyTo(target[4..]);
        }
        else
        {
            addr.CopyTo(target[8..]);
            BitConverter.TryWriteBytes(target[24..], (uint)endpoint.Address.ScopeId);
        }
    }

    /// <summary>Reads the first peer's LastHandshake (100ns since 1601) and byte counters from a GetConfiguration buffer.</summary>
    public static (DateTime? LastHandshakeUtc, ulong Rx, ulong Tx) ReadFirstPeer(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < InterfaceSize + PeerSize || BitConverter.ToUInt32(buffer[72..]) < 1)
        {
            return (null, 0, 0);
        }

        var peer = buffer[InterfaceSize..];
        var tx = BitConverter.ToUInt64(peer[104..]);
        var rx = BitConverter.ToUInt64(peer[112..]);
        var hs = BitConverter.ToUInt64(peer[120..]);
        DateTime? handshake = hs == 0 ? null : DateTime.FromFileTimeUtc((long)hs);
        return (handshake, rx, tx);
    }

    /// <summary>Parses "addr/len" tolerating host bits (as WireGuard configs do); returns the masked network and the written address.</summary>
    public static (IPNetwork Network, IPAddress Address, int Prefix) ParseCidr(string text)
    {
        var parts = text.Trim().Split('/');
        var ip = IPAddress.Parse(parts[0].Trim());
        var max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var prefix = parts.Length > 1 ? int.Parse(parts[1]) : max;
        if (prefix < 0 || prefix > max) throw new FormatException("CIDR prefix out of range.");
        var bytes = ip.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var bits = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - bits));
        }

        return (new IPNetwork(new IPAddress(bytes), prefix), ip, prefix);
    }

    public static byte[] DecodeKey(string base64)
    {
        var key = Convert.FromBase64String(base64.Trim());
        if (key.Length != KeyLength) throw new FormatException("WireGuard key must decode to 32 bytes.");
        return key;
    }

    /// <summary>Accepts "ip:port", "[v6]:port" or "host:port" (resolved; first address wins).</summary>
    public static IPEndPoint ParseEndpoint(string endpoint)
    {
        var s = endpoint.Trim();
        var colon = s.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(s[(colon + 1)..], out var port) || port is < 1 or > 65535)
        {
            throw new FormatException("Endpoint must be host:port.");
        }

        var host = s[..colon].Trim('[', ']');
        if (IPAddress.TryParse(host, out var ip)) return new IPEndPoint(ip, port);
        var resolved = Dns.GetHostAddresses(host);
        if (resolved.Length == 0) throw new FormatException("Endpoint host could not be resolved.");
        return new IPEndPoint(resolved[0], port);
    }

    public static string AdapterNameFor(string clientId) =>
        "SecureTunnel-" + new string(clientId.Where(char.IsLetterOrDigit).Take(24).ToArray());

    public static Guid AdapterGuidFor(string adapterName) =>
        new(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("SecureTunnel/" + adapterName))[..16]);
}
