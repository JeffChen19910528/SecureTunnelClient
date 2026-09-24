using System.Net;

namespace SecureTunnel.Client.Core.Validation;

/// <summary>
/// Validates a WireGuard peer endpoint string ("host:port" or
/// "[ipv6]:port"). Shared by the configuration parser and any other
/// caller that needs to validate an endpoint independently.
/// </summary>
public static class PeerEndpointValidator
{
    public static ValidationIssue? Validate(string field, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new ValidationIssue(field, ValidationSeverity.Error, "Endpoint must not be empty.");
        }

        string hostPart;
        string portPart;

        if (value.StartsWith('['))
        {
            var closingBracket = value.IndexOf(']');
            if (closingBracket < 0 || closingBracket + 1 >= value.Length || value[closingBracket + 1] != ':')
            {
                return new ValidationIssue(field, ValidationSeverity.Error, "Endpoint is not a valid 'host:port' value.");
            }

            hostPart = value[1..closingBracket];
            portPart = value[(closingBracket + 2)..];
        }
        else
        {
            var lastColon = value.LastIndexOf(':');
            if (lastColon <= 0 || lastColon == value.Length - 1)
            {
                return new ValidationIssue(field, ValidationSeverity.Error, "Endpoint is not a valid 'host:port' value.");
            }

            hostPart = value[..lastColon];
            portPart = value[(lastColon + 1)..];
        }

        if (!int.TryParse(portPart, out var port) || port is < 1 or > 65535)
        {
            return new ValidationIssue(field, ValidationSeverity.Error, "Endpoint port must be an integer between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(hostPart) || !IsValidHost(hostPart))
        {
            return new ValidationIssue(field, ValidationSeverity.Error, "Endpoint host is not a valid IP address or hostname.");
        }

        return null;
    }

    private static bool IsValidHost(string host)
    {
        if (IPAddress.TryParse(host, out _))
        {
            return true;
        }

        // Hostname: labels of letters/digits/hyphens separated by dots,
        // no leading/trailing hyphen per label.
        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > 63)
            {
                return false;
            }

            if (label.StartsWith('-') || label.EndsWith('-'))
            {
                return false;
            }

            foreach (var c in label)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }
}
