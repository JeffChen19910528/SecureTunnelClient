using System.Net;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.Infrastructure.Configuration;
using SecureTunnel.Client.Infrastructure.WireGuard.Embedded;
using SecureTunnel.Client.TestSupport;

namespace SecureTunnel.Client.Infrastructure.Tests.WireGuard;

public class EmbeddedWireGuardTests
{
    private const string PrivateKey = "hyCu4faV33FeOhqiRoy9bRSV30qH/XO0K98N3Mg7uD8=";
    private const string PublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=";
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FakeTunnel : IWireGuardNativeTunnel
    {
        public bool Available { get; set; } = true;
        public NativeTunnelResult UpResult { get; set; } = new(true);
        public NativeTunnelSnapshot? Snapshot { get; set; }
        public List<string> Calls { get; } = new();

        public bool IsAvailable(out string? reason) { reason = Available ? null : "missing"; return Available; }
        public NativeTunnelResult Up(string adapterName, ClientConfiguration c, SensitiveString k, SensitiveString? p) { Calls.Add("up:" + adapterName); return UpResult; }
        public NativeTunnelResult Down(string adapterName) { Calls.Add("down:" + adapterName); Snapshot = null; return new(true); }
        public NativeTunnelSnapshot? Query(string adapterName) => Snapshot;
    }

    private static ClientConfiguration Config() => new()
    {
        ClientId = "client-1",
        GatewayName = "gw",
        GatewayEndpoint = "10.0.0.1:51820",
        InterfaceAddress = "10.250.0.10/32",
        PeerPublicKey = PublicKey,
        PeerEndpoint = "10.0.0.1:51820",
        AllowedIPs = ["10.20.0.0/24"],
        PersistentKeepalive = 25,
    };

    private static async Task<(EmbeddedWireGuardClientService Service, FakeTunnel Tunnel, ClientConfiguration Config)> Create()
    {
        var store = new FakeClientConfigurationStore();
        var config = Config();
        await store.SaveAsync(config, new SensitiveString(PrivateKey), default);
        var tunnel = new FakeTunnel();
        var service = new EmbeddedWireGuardClientService(tunnel, new WireGuardConfigTextParser(), store, new FakeProcessRunner(), () => Now);
        return (service, tunnel, config);
    }

    [Fact]
    public void ConfigBuilder_WritesWireguardNtLayout()
    {
        var priv = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var pub = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();
        var allowed = new[] { WireGuardNtConfigBuilder.ParseCidr("10.20.0.0/24").Network };

        var buf = WireGuardNtConfigBuilder.Build(priv, pub, null, new IPEndPoint(IPAddress.Parse("203.0.113.5"), 51820), 25, allowed);

        Assert.Equal(WireGuardNtConfigBuilder.InterfaceSize + WireGuardNtConfigBuilder.PeerSize + WireGuardNtConfigBuilder.AllowedIpSize, buf.Length);
        Assert.Equal(2u | 8u, BitConverter.ToUInt32(buf, 0));          // HAS_PRIVATE_KEY | REPLACE_PEERS
        Assert.Equal(priv, buf[6..38]);
        Assert.Equal(1u, BitConverter.ToUInt32(buf, 72));              // PeersCount
        var peer = 80;
        Assert.Equal(1u | 4u | 8u | 32u, BitConverter.ToUInt32(buf, peer));
        Assert.Equal(pub, buf[(peer + 8)..(peer + 40)]);
        Assert.Equal((ushort)25, BitConverter.ToUInt16(buf, peer + 72));
        Assert.Equal((ushort)2, BitConverter.ToUInt16(buf, peer + 76)); // AF_INET
        Assert.Equal(new byte[] { 0xCA, 0x6C }, buf[(peer + 78)..(peer + 80)]); // port 51820 big-endian
        Assert.Equal(new byte[] { 203, 0, 113, 5 }, buf[(peer + 80)..(peer + 84)]);
        Assert.Equal(1u, BitConverter.ToUInt32(buf, peer + 128));
        var ip = peer + 136;
        Assert.Equal(new byte[] { 10, 20, 0, 0 }, buf[ip..(ip + 4)]);
        Assert.Equal((ushort)2, BitConverter.ToUInt16(buf, ip + 16));
        Assert.Equal(24, buf[ip + 18]);
    }

    [Fact]
    public void ParseCidr_MasksHostBits()
    {
        var (net, addr, prefix) = WireGuardNtConfigBuilder.ParseCidr("10.20.0.77/24");
        Assert.Equal("10.20.0.0", net.BaseAddress.ToString());
        Assert.Equal("10.20.0.77", addr.ToString());
        Assert.Equal(24, prefix);
    }

    [Fact]
    public void ReadFirstPeer_ReturnsHandshakeAndCounters()
    {
        var buf = new byte[WireGuardNtConfigBuilder.InterfaceSize + WireGuardNtConfigBuilder.PeerSize];
        BitConverter.TryWriteBytes(buf.AsSpan(72), 1u);
        BitConverter.TryWriteBytes(buf.AsSpan(80 + 104), 11UL);
        BitConverter.TryWriteBytes(buf.AsSpan(80 + 112), 22UL);
        BitConverter.TryWriteBytes(buf.AsSpan(80 + 120), (ulong)Now.ToFileTimeUtc());

        var (handshake, rx, tx) = WireGuardNtConfigBuilder.ReadFirstPeer(buf);

        Assert.Equal(Now, handshake);
        Assert.Equal(22UL, rx);
        Assert.Equal(11UL, tx);
    }

    [Fact]
    public async Task Status_NoAdapter_IsDisconnected()
    {
        var (service, _, config) = await Create();
        var status = await service.GetStatusAsync(config, default);
        Assert.Equal(WireGuardOutcome.Disconnected, status.Outcome);
    }

    [Fact]
    public async Task Status_NoHandshake_IsAwaitingHandshake_NeverConnected()
    {
        var (service, tunnel, config) = await Create();
        tunnel.Snapshot = new NativeTunnelSnapshot(null, 0, 148);
        var status = await service.GetStatusAsync(config, default);
        Assert.Equal(WireGuardOutcome.AwaitingHandshake, status.Outcome);
    }

    [Fact]
    public async Task Status_RecentHandshake_IsConnected()
    {
        var (service, tunnel, config) = await Create();
        tunnel.Snapshot = new NativeTunnelSnapshot(Now.AddSeconds(-30), 1, 1);
        var status = await service.GetStatusAsync(config, default);
        Assert.Equal(WireGuardOutcome.Connected, status.Outcome);
        Assert.Equal(ClientState.Connected, status.ObservedState);
    }

    [Fact]
    public async Task Status_StaleHandshake_IsNotConnected()
    {
        var (service, tunnel, config) = await Create();
        tunnel.Snapshot = new NativeTunnelSnapshot(Now.AddMinutes(-10), 1, 1);
        var status = await service.GetStatusAsync(config, default);
        Assert.Equal(WireGuardOutcome.AwaitingHandshake, status.Outcome);
    }

    [Fact]
    public async Task Connect_Success_DoesNotClaimConnected()
    {
        var (service, tunnel, config) = await Create();
        var result = await service.ConnectAsync(config, default);
        Assert.True(result.Success);
        Assert.NotEqual(WireGuardOutcome.Connected, result.Outcome);
        Assert.Contains("up:SecureTunnel-client1", tunnel.Calls);
    }

    [Fact]
    public async Task Connect_EngineMissing_ReportsUnavailable()
    {
        var (service, tunnel, config) = await Create();
        tunnel.Available = false;
        var result = await service.ConnectAsync(config, default);
        Assert.False(result.Success);
        Assert.Equal("EngineUnavailable", result.ErrorCode);
    }

    [Theory]
    [InlineData("PermissionDenied", WireGuardOutcome.PermissionDenied)]
    [InlineData("FullTunnelUnsupported", WireGuardOutcome.InvalidConfiguration)]
    [InlineData("AdapterCreateFailed", WireGuardOutcome.ConnectionFailed)]
    public async Task Connect_Failures_MapToOutcomes(string code, WireGuardOutcome expected)
    {
        var (service, tunnel, config) = await Create();
        tunnel.UpResult = new NativeTunnelResult(false, code, "detail");
        var result = await service.ConnectAsync(config, default);
        Assert.False(result.Success);
        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task Disconnect_IsIdempotent()
    {
        var (service, tunnel, config) = await Create();
        Assert.True((await service.DisconnectAsync(config, default)).Success);
        Assert.True((await service.DisconnectAsync(config, default)).Success);
        Assert.Equal(2, tunnel.Calls.Count(c => c.StartsWith("down:")));
    }
}
