using SecureTunnel.Client.Infrastructure.Configuration;

namespace SecureTunnel.Client.Infrastructure.Tests.Configuration;

public class WireGuardConfigTextParserTests
{
    // Test-fixture-only keys, generated locally for these tests. Never real
    // WireGuard key material.
    private const string PrivateKey = "hyCu4faV33FeOhqiRoy9bRSV30qH/XO0K98N3Mg7uD8=";
    private const string PublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=";
    private const string PresharedKey = "9mYFo8H6H4E86EBXubkPidkaQCAvowbF/eZAWm+DJVw=";

    private static string ValidConfig() => $"""
        [Interface]
        PrivateKey = {PrivateKey}
        Address = 10.0.0.2/32
        DNS = 1.1.1.1
        MTU = 1420

        [Peer]
        PublicKey = {PublicKey}
        AllowedIPs = 10.10.0.0/24, 10.20.0.0/24
        Endpoint = gateway.example.com:51820
        PersistentKeepalive = 25
        """;

    [Fact]
    public void Parse_ValidConfig_ReturnsSuccess()
    {
        var parser = new WireGuardConfigTextParser();

        var result = parser.Parse(ValidConfig());

        Assert.True(result.Success);
        Assert.NotNull(result.Configuration);
        Assert.NotNull(result.PrivateKey);
        Assert.Equal(PrivateKey, result.PrivateKey!.Reveal());
        Assert.True(result.Validation.IsValid);
    }

    [Fact]
    public void Parse_FullInterfaceAndPeer_PopulatesAllFields()
    {
        var parser = new WireGuardConfigTextParser();

        var result = parser.Parse(ValidConfig());

        Assert.True(result.Success);
        var config = result.Configuration!;
        Assert.Equal("10.0.0.2/32", config.InterfaceAddress);
        Assert.Equal(1420, config.Mtu);
        Assert.Equal(["1.1.1.1"], config.DnsServers);
        Assert.Equal(PublicKey, config.PeerPublicKey);
        Assert.Equal("gateway.example.com:51820", config.PeerEndpoint);
        Assert.Equal(["10.10.0.0/24", "10.20.0.0/24"], config.AllowedIPs);
        Assert.Equal(25, config.PersistentKeepalive);
    }

    [Fact]
    public void Parse_PresharedKey_ReturnsSensitiveString_NotInToString()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            PresharedKey = {PresharedKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.True(result.Success);
        Assert.NotNull(result.PresharedKey);
        Assert.Equal(PresharedKey, result.PresharedKey!.Reveal());
        Assert.DoesNotContain(PresharedKey, result.PresharedKey.ToString());
        Assert.Equal("***REDACTED***", result.PresharedKey.ToString());
    }

    [Fact]
    public void Parse_MissingInterfaceSection_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Interface");
    }

    [Fact]
    public void Parse_MissingPeerSection_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Peer");
    }

    [Fact]
    public void Parse_MissingPrivateKey_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Interface.PrivateKey");
    }

    [Fact]
    public void Parse_MissingPeerPublicKey_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Peer.PublicKey");
        Assert.Null(result.Configuration);
    }

    [Fact]
    public void Parse_InvalidKeyFormat_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = not-a-valid-key
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Interface.PrivateKey");
        Assert.Null(result.PrivateKey);
    }

    [Fact]
    public void Parse_InvalidIpAddress_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = not-an-ip

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Interface.Address");
    }

    [Fact]
    public void Parse_InvalidAllowedIPs_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = not-a-cidr
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Peer.AllowedIPs");
    }

    [Fact]
    public void Parse_MissingAllowedIPs_ReturnsError_NeverDefaults()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Peer.AllowedIPs");
        Assert.Null(result.Configuration);
    }

    [Fact]
    public void Parse_MissingPeerEndpoint_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Peer.Endpoint");
    }

    [Fact]
    public void Parse_InvalidPeerEndpointShape_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = not-a-valid-endpoint
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Peer.Endpoint");
    }

    [Fact]
    public void Parse_MalformedSyntax_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = """
            [Interface]
            ThisLineHasNoEqualsSign
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Document");
    }

    [Fact]
    public void Parse_DuplicateFields_ReturnsError()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.False(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Interface.PrivateKey" && i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Parse_UnsupportedFields_ReturnsWarning()
    {
        var parser = new WireGuardConfigTextParser();
        var text = $"""
            [Interface]
            PrivateKey = {PrivateKey}
            Address = 10.0.0.2/32
            SomeUnsupportedField = 1

            [Peer]
            PublicKey = {PublicKey}
            AllowedIPs = 10.10.0.0/24
            Endpoint = gateway.example.com:51820
            """;

        var result = parser.Parse(text);

        Assert.True(result.Success);
        Assert.Contains(result.Validation.Issues, i => i.Field == "Interface.SomeUnsupportedField");
    }

    [Fact]
    public void Parse_NeverMutatesInputString()
    {
        var parser = new WireGuardConfigTextParser();
        var original = ValidConfig();
        var copy = new string(original.ToCharArray());

        parser.Parse(original);

        Assert.Equal(copy, original);
    }
}
