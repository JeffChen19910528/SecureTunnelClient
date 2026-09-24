using System.Net;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.Core.Validation;

namespace SecureTunnel.Client.Infrastructure.Configuration;

/// <summary>
/// Parses standard WireGuard client configuration text ([Interface] /
/// [Peer] sections). Never mutates the input string, never returns the
/// private key or preshared key as a plain string, never silently
/// defaults or widens AllowedIPs, and never treats a malformed document
/// as partially valid - any structural error produces Success=false with
/// a populated <see cref="ValidationResult"/>.
/// </summary>
public sealed class WireGuardConfigTextParser : IClientConfigurationParser
{
    private static readonly string[] SupportedInterfaceKeys = ["PrivateKey", "Address", "DNS", "ListenPort", "MTU"];
    private static readonly string[] SupportedPeerKeys = ["PublicKey", "PresharedKey", "AllowedIPs", "Endpoint", "PersistentKeepalive"];

    public ParsedConfigurationResult Parse(string rawConfigText)
    {
        var validation = new ValidationResult();

        if (string.IsNullOrWhiteSpace(rawConfigText))
        {
            validation.AddError("Document", "Configuration text is empty.");
            return Failure(validation);
        }

        var sections = SplitIntoSections(rawConfigText, validation);
        if (!validation.IsValid)
        {
            return Failure(validation);
        }

        var interfaceSection = sections.FirstOrDefault(s => s.Name == "Interface");
        var peerSection = sections.FirstOrDefault(s => s.Name == "Peer");

        if (interfaceSection is null)
        {
            validation.AddError("Interface", "Configuration is missing an [Interface] section.");
        }

        if (peerSection is null)
        {
            validation.AddError("Peer", "Configuration is missing a [Peer] section.");
        }

        if (!validation.IsValid)
        {
            return Failure(validation);
        }

        ValidateNoDuplicateKeys(interfaceSection!, "Interface", validation);
        ValidateNoDuplicateKeys(peerSection!, "Peer", validation);
        ValidateSupportedKeys(interfaceSection!, "Interface", SupportedInterfaceKeys, validation);
        ValidateSupportedKeys(peerSection!, "Peer", SupportedPeerKeys, validation);

        var interfaceValues = ToLastValueMap(interfaceSection!);
        var peerValues = ToLastValueMap(peerSection!);

        SensitiveString? privateKey = null;
        if (!interfaceValues.TryGetValue("PrivateKey", out var privateKeyRaw) || string.IsNullOrWhiteSpace(privateKeyRaw))
        {
            validation.AddError("Interface.PrivateKey", "Configuration is missing a private key.");
        }
        else if (!IsValidWireGuardKey(privateKeyRaw))
        {
            validation.AddError("Interface.PrivateKey", "Private key is not a valid WireGuard key (expected 44-character base64).");
        }
        else
        {
            privateKey = new SensitiveString(privateKeyRaw);
        }

        if (!interfaceValues.TryGetValue("Address", out var addressRaw) || string.IsNullOrWhiteSpace(addressRaw))
        {
            validation.AddError("Interface.Address", "Configuration is missing an interface address.");
        }
        else if (!IsValidCidrOrIpList(addressRaw))
        {
            validation.AddError("Interface.Address", "Interface address is not a valid IP/CIDR value.");
        }

        if (!peerValues.TryGetValue("PublicKey", out var publicKeyRaw) || string.IsNullOrWhiteSpace(publicKeyRaw))
        {
            validation.AddError("Peer.PublicKey", "Configuration is missing the peer's public key.");
        }
        else if (!IsValidWireGuardKey(publicKeyRaw))
        {
            validation.AddError("Peer.PublicKey", "Peer public key is not a valid WireGuard key (expected 44-character base64).");
        }

        SensitiveString? presharedKey = null;
        if (peerValues.TryGetValue("PresharedKey", out var presharedKeyRaw) && !string.IsNullOrWhiteSpace(presharedKeyRaw))
        {
            if (!IsValidWireGuardKey(presharedKeyRaw))
            {
                validation.AddError("Peer.PresharedKey", "Preshared key is not a valid WireGuard key (expected 44-character base64).");
            }
            else
            {
                presharedKey = new SensitiveString(presharedKeyRaw);
            }
        }

        IReadOnlyList<string> allowedIPs = [];
        if (!peerValues.TryGetValue("AllowedIPs", out var allowedIPsRaw) || string.IsNullOrWhiteSpace(allowedIPsRaw))
        {
            validation.AddError("Peer.AllowedIPs", "Configuration is missing AllowedIPs.");
        }
        else
        {
            var candidates = allowedIPsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (candidates.Length == 0 || candidates.Any(c => !IsValidCidrOrIpList(c)))
            {
                validation.AddError("Peer.AllowedIPs", "AllowedIPs contains an invalid IP/CIDR entry.");
            }
            else
            {
                allowedIPs = candidates;
            }
        }

        string? endpoint = null;
        if (!peerValues.TryGetValue("Endpoint", out var endpointRaw) || string.IsNullOrWhiteSpace(endpointRaw))
        {
            validation.AddError("Peer.Endpoint", "Configuration is missing the peer endpoint.");
        }
        else
        {
            var endpointIssue = PeerEndpointValidator.Validate("Peer.Endpoint", endpointRaw);
            if (endpointIssue is not null)
            {
                validation.AddError(endpointIssue.Field, endpointIssue.Message, endpointIssue.FixHint);
            }
            else
            {
                endpoint = endpointRaw;
            }
        }

        int? persistentKeepalive = null;
        if (peerValues.TryGetValue("PersistentKeepalive", out var keepaliveRaw) && !string.IsNullOrWhiteSpace(keepaliveRaw))
        {
            if (int.TryParse(keepaliveRaw, out var parsedKeepalive) && parsedKeepalive >= 0)
            {
                persistentKeepalive = parsedKeepalive;
            }
            else
            {
                validation.AddError("Peer.PersistentKeepalive", "PersistentKeepalive must be a non-negative integer.");
            }
        }

        List<string>? dnsServers = null;
        if (interfaceValues.TryGetValue("DNS", out var dnsRaw) && !string.IsNullOrWhiteSpace(dnsRaw))
        {
            dnsServers = dnsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        int? mtu = null;
        if (interfaceValues.TryGetValue("MTU", out var mtuRaw) && !string.IsNullOrWhiteSpace(mtuRaw))
        {
            if (int.TryParse(mtuRaw, out var parsedMtu) && parsedMtu > 0)
            {
                mtu = parsedMtu;
            }
            else
            {
                validation.AddError("Interface.MTU", "MTU must be a positive integer.");
            }
        }

        if (!validation.IsValid)
        {
            return Failure(validation);
        }

        var configuration = new ClientConfiguration
        {
            ClientId = Guid.NewGuid().ToString("N"),
            GatewayName = "Imported",
            GatewayEndpoint = endpoint!,
            InterfaceAddress = addressRaw!,
            DnsServers = dnsServers,
            Mtu = mtu,
            PeerPublicKey = publicKeyRaw!,
            PresharedKey = presharedKey,
            PeerEndpoint = endpoint!,
            AllowedIPs = allowedIPs,
            PersistentKeepalive = persistentKeepalive,
            ConfigurationSource = ConfigurationSource.Imported,
            ImportedAtUtc = DateTime.UtcNow,
            Status = ConfigurationStatus.Unverified
        };

        return new ParsedConfigurationResult
        {
            Success = true,
            Configuration = configuration,
            PrivateKey = privateKey,
            PresharedKey = presharedKey,
            Validation = validation
        };
    }

    private static ParsedConfigurationResult Failure(ValidationResult validation) => new()
    {
        Success = false,
        Configuration = null,
        PrivateKey = null,
        Validation = validation
    };

    private sealed record ConfigSection(string Name, List<(string Key, string Value)> Entries);

    private static List<ConfigSection> SplitIntoSections(string rawConfigText, ValidationResult validation)
    {
        var sections = new List<ConfigSection>();
        ConfigSection? current = null;

        var lines = rawConfigText.Replace("\r\n", "\n").Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var name = line[1..^1].Trim();
                if (name is not ("Interface" or "Peer"))
                {
                    validation.AddError("Document", $"Unsupported configuration section '[{name}]'.");
                    continue;
                }

                current = new ConfigSection(name, []);
                sections.Add(current);
                continue;
            }

            if (current is null)
            {
                validation.AddError("Document", $"Configuration entry found outside of any section: '{line}'.");
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                validation.AddError("Document", $"Malformed configuration line (expected 'Key = Value'): '{line}'.");
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            current.Entries.Add((key, value));
        }

        return sections;
    }

    private static void ValidateNoDuplicateKeys(ConfigSection section, string sectionName, ValidationResult validation)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, _) in section.Entries)
        {
            if (!seen.Add(key))
            {
                validation.AddError($"{sectionName}.{key}", $"Duplicate field '{key}' in [{sectionName}] section.");
            }
        }
    }

    private static void ValidateSupportedKeys(ConfigSection section, string sectionName, string[] supportedKeys, ValidationResult validation)
    {
        foreach (var (key, _) in section.Entries)
        {
            if (!supportedKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                validation.AddWarning($"{sectionName}.{key}", $"Unsupported field '{key}' in [{sectionName}] section was ignored.");
            }
        }
    }

    private static Dictionary<string, string> ToLastValueMap(ConfigSection section)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in section.Entries)
        {
            map[key] = value;
        }

        return map;
    }

    private static bool IsValidWireGuardKey(string value) =>
        value.Length == 44 && value.EndsWith('=') && IsBase64(value);

    private static bool IsBase64(string value)
    {
        Span<byte> buffer = stackalloc byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out _);
    }

    private static bool IsValidCidrOrIpList(string value)
    {
        var parts = value.Split('/');
        if (parts.Length is not (1 or 2))
        {
            return false;
        }

        if (!IPAddress.TryParse(parts[0], out _))
        {
            return false;
        }

        if (parts.Length == 2 && (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 128))
        {
            return false;
        }

        return true;
    }
}
