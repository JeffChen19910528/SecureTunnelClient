namespace SecureTunnel.Client.Core.Configuration;

/// <summary>
/// Parses raw WireGuard client configuration text into a structured
/// <see cref="ParsedConfigurationResult"/>. Implementations must never
/// silently modify the imported configuration and must be testable without
/// WireGuard being installed.
/// </summary>
public interface IClientConfigurationParser
{
    ParsedConfigurationResult Parse(string rawConfigText);
}
