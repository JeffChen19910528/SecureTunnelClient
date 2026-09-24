using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Infrastructure.WireGuard;

/// <summary>
/// Parses the tab-separated output of <c>wireguard.exe /dumptunnelservice &lt;name&gt;</c>
/// to distinguish interface-up-but-no-peer, peer-present-but-no-handshake,
/// and a confirmed handshake. Per WireGuard's dump format, the first line
/// describes the interface (private-key, public-key, listen-port, fwmark)
/// and each subsequent line describes one peer
/// (public-key, preshared-key, endpoint, allowed-ips, latest-handshake,
/// transfer-rx, transfer-tx, persistent-keepalive - tab-separated,
/// latest-handshake is a Unix epoch second count, 0 meaning "never").
/// The interface line's private key is never returned or logged - it is
/// discarded immediately after the interface line is located.
/// </summary>
public static class WireGuardDumpParser
{
    public static WireGuardOutcome Parse(bool commandStarted, int exitCode, string standardOutput)
    {
        if (!commandStarted || exitCode != 0)
        {
            return WireGuardOutcome.StatusUnavailable;
        }

        var lines = standardOutput
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (lines.Count == 0)
        {
            return WireGuardOutcome.Disconnected;
        }

        // First line is the interface line; discard it immediately (it
        // contains the private key) - only its presence matters here.
        if (lines.Count == 1)
        {
            return WireGuardOutcome.InterfaceActive;
        }

        // Second line onward are peer lines. Take the first peer's
        // latest-handshake field (index 4).
        var peerFields = lines[1].Split('\t');
        if (peerFields.Length < 5)
        {
            return WireGuardOutcome.StatusUnavailable;
        }

        if (!long.TryParse(peerFields[4], out var latestHandshakeEpochSeconds))
        {
            return WireGuardOutcome.StatusUnavailable;
        }

        return latestHandshakeEpochSeconds > 0
            ? WireGuardOutcome.Connected
            : WireGuardOutcome.AwaitingHandshake;
    }
}
