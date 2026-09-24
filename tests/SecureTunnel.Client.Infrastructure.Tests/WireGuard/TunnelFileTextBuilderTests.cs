using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.Infrastructure.Configuration;
using SecureTunnel.Client.Infrastructure.WireGuard;

namespace SecureTunnel.Client.Infrastructure.Tests.WireGuard;

public class TunnelFileTextBuilderTests
{
    private const string PrivateKey = "hyCu4faV33FeOhqiRoy9bRSV30qH/XO0K98N3Mg7uD8=";
    private const string PublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=";
    private const string PresharedKey = "9mYFo8H6H4E86EBXubkPidkaQCAvowbF/eZAWm+DJVw=";

    [Fact]
    public void Build_ProducesInterfaceAndPeerSections()
    {
        var config = new ClientConfiguration
        {
            ClientId = "client-1",
            GatewayName = "Gateway",
            GatewayEndpoint = "gateway.example.com:51820",
            InterfaceAddress = "10.0.0.2/32",
            Mtu = 1420,
            DnsServers = ["1.1.1.1"],
            PeerPublicKey = PublicKey,
            PeerEndpoint = "gateway.example.com:51820",
            AllowedIPs = ["10.10.0.0/24"],
            PersistentKeepalive = 25
        };

        var text = TunnelFileTextBuilder.Build(config, new SensitiveString(PrivateKey), new SensitiveString(PresharedKey));

        Assert.Contains("[Interface]", text);
        Assert.Contains("[Peer]", text);
        Assert.Contains($"PrivateKey = {PrivateKey}", text);
        Assert.Contains($"PublicKey = {PublicKey}", text);
        Assert.Contains($"PresharedKey = {PresharedKey}", text);
        Assert.Contains("Endpoint = gateway.example.com:51820", text);
        Assert.Contains("AllowedIPs = 10.10.0.0/24", text);
        Assert.Contains("PersistentKeepalive = 25", text);
        Assert.Contains("MTU = 1420", text);
    }

    [Fact]
    public void Generate_RoundTrip_ProducesEquivalentDocument()
    {
        var parser = new WireGuardConfigTextParser();
        var originalText = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32
            DNS = 1.1.1.1
            MTU = 1420

            [Peer]
            PublicKey = {PublicKey}
            PresharedKey = {PresharedKey}
            AllowedIPs = 10.10.0.0/24, 10.20.0.0/24
            Endpoint = gateway.example.com:51820
            PersistentKeepalive = 25
            """;

        var parsed = parser.Parse(originalText);
        Assert.True(parsed.Success);

        var generatedText = TunnelFileTextBuilder.Build(parsed.Configuration!, parsed.PrivateKey!, parsed.PresharedKey);
        var reparsed = parser.Parse(generatedText);

        Assert.True(reparsed.Success);
        Assert.Equal(parsed.Configuration!.InterfaceAddress, reparsed.Configuration!.InterfaceAddress);
        Assert.Equal(parsed.Configuration.PeerPublicKey, reparsed.Configuration.PeerPublicKey);
        Assert.Equal(parsed.Configuration.PeerEndpoint, reparsed.Configuration.PeerEndpoint);
        Assert.Equal(parsed.Configuration.AllowedIPs, reparsed.Configuration.AllowedIPs);
        Assert.Equal(parsed.Configuration.PersistentKeepalive, reparsed.Configuration.PersistentKeepalive);
        Assert.Equal(parsed.Configuration.Mtu, reparsed.Configuration.Mtu);
        Assert.Equal(parsed.PrivateKey!.Reveal(), reparsed.PrivateKey!.Reveal());
        Assert.Equal(parsed.PresharedKey!.Reveal(), reparsed.PresharedKey!.Reveal());
    }

    [Fact]
    public void Build_NeverIncludesKeyMaterial_InToStringOfSensitiveTypes()
    {
        var config = new ClientConfiguration
        {
            ClientId = "client-1",
            GatewayName = "Gateway",
            GatewayEndpoint = "gateway.example.com:51820",
            InterfaceAddress = "10.0.0.2/32",
            PeerPublicKey = PublicKey,
            PeerEndpoint = "gateway.example.com:51820",
            AllowedIPs = ["10.10.0.0/24"]
        };

        var privateKey = new SensitiveString(PrivateKey);
        var text = TunnelFileTextBuilder.Build(config, privateKey, presharedKey: null);

        // The builder's *output* legitimately contains the plaintext key
        // (that's its job - it's only ever written to a short-lived,
        // ACL-tight temp file). What must never leak is the key via the
        // SensitiveString's own ToString().
        Assert.Equal("***REDACTED***", privateKey.ToString());
        Assert.Contains(PrivateKey, text);
    }
}
