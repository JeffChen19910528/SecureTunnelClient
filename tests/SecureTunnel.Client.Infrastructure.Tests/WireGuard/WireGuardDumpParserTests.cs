using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.Infrastructure.WireGuard;

namespace SecureTunnel.Client.Infrastructure.Tests.WireGuard;

public class WireGuardDumpParserTests
{
    private const string PublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=";
    private const string PrivateKeyLine = "hyCu4faV33FeOhqiRoy9bRSV30qH/XO0K98N3Mg7uD8=\tR+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=\t0\t0";

    [Fact]
    public void Parse_CommandNotStarted_ReturnsStatusUnavailable()
    {
        var outcome = WireGuardDumpParser.Parse(commandStarted: false, exitCode: -1, standardOutput: string.Empty);

        Assert.Equal(WireGuardOutcome.StatusUnavailable, outcome);
    }

    [Fact]
    public void Parse_NonZeroExitCode_ReturnsStatusUnavailable()
    {
        var outcome = WireGuardDumpParser.Parse(commandStarted: true, exitCode: 1, standardOutput: PrivateKeyLine);

        Assert.Equal(WireGuardOutcome.StatusUnavailable, outcome);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsDisconnected()
    {
        var outcome = WireGuardDumpParser.Parse(commandStarted: true, exitCode: 0, standardOutput: string.Empty);

        Assert.Equal(WireGuardOutcome.Disconnected, outcome);
    }

    [Fact]
    public void Parse_InterfaceLineOnly_ReturnsInterfaceActive()
    {
        var outcome = WireGuardDumpParser.Parse(commandStarted: true, exitCode: 0, standardOutput: PrivateKeyLine);

        Assert.Equal(WireGuardOutcome.InterfaceActive, outcome);
    }

    [Fact]
    public void Parse_PeerWithZeroHandshake_ReturnsAwaitingHandshake()
    {
        var peerLine = $"{PublicKey}\t\t203.0.113.1:51820\t0.0.0.0/0\t0\t0\t0\t0";
        var output = $"{PrivateKeyLine}\n{peerLine}";

        var outcome = WireGuardDumpParser.Parse(commandStarted: true, exitCode: 0, standardOutput: output);

        Assert.Equal(WireGuardOutcome.AwaitingHandshake, outcome);
    }

    [Fact]
    public void Parse_PeerWithNonZeroHandshake_ReturnsConnected()
    {
        var peerLine = $"{PublicKey}\t\t203.0.113.1:51820\t0.0.0.0/0\t1700000000\t0\t0\t0";
        var output = $"{PrivateKeyLine}\n{peerLine}";

        var outcome = WireGuardDumpParser.Parse(commandStarted: true, exitCode: 0, standardOutput: output);

        Assert.Equal(WireGuardOutcome.Connected, outcome);
    }

    [Fact]
    public void Parse_MalformedPeerLine_ReturnsStatusUnavailable()
    {
        var output = $"{PrivateKeyLine}\ntoo\tfew\tfields";

        var outcome = WireGuardDumpParser.Parse(commandStarted: true, exitCode: 0, standardOutput: output);

        Assert.Equal(WireGuardOutcome.StatusUnavailable, outcome);
    }

    [Fact]
    public void Parse_NeverReturnsOrExposesThePrivateKeyLine()
    {
        var peerLine = $"{PublicKey}\t\t203.0.113.1:51820\t0.0.0.0/0\t1700000000\t0\t0\t0";
        var output = $"{PrivateKeyLine}\n{peerLine}";

        // The parser's only output is an enum value - there is no code
        // path that could return the raw interface line's private key.
        var outcome = WireGuardDumpParser.Parse(commandStarted: true, exitCode: 0, standardOutput: output);

        Assert.IsType<WireGuardOutcome>(outcome);
    }
}
