using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.Core.Validation;

namespace SecureTunnel.Client.Core.Configuration;

/// <summary>
/// Result of parsing raw WireGuard configuration text. <see cref="PrivateKey"/>
/// and <see cref="PresharedKey"/> are only populated on success and must
/// never be logged; callers should hand them directly to secure storage.
/// </summary>
public sealed class ParsedConfigurationResult
{
    public required bool Success { get; init; }

    public ClientConfiguration? Configuration { get; init; }

    public SensitiveString? PrivateKey { get; init; }

    public SensitiveString? PresharedKey { get; init; }

    public required ValidationResult Validation { get; init; }
}
