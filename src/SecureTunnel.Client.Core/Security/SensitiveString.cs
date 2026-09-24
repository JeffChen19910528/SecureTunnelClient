namespace SecureTunnel.Client.Core.Security;

/// <summary>
/// Wraps a secret value (e.g. a WireGuard private key) so it cannot be
/// accidentally written to logs, exceptions, console output, or string
/// interpolation. Call <see cref="Reveal"/> only at the point of actual use
/// (e.g. handing the key to a secure storage or process boundary) and never
/// pass the revealed value to a logger, exception message, or command-line
/// argument.
/// </summary>
public sealed class SensitiveString
{
    private readonly string _value;

    public SensitiveString(string value)
    {
        _value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Returns the raw secret value. Callers must never log, print, or
    /// otherwise persist the returned value outside of its intended
    /// protected destination (e.g. DPAPI-encrypted storage or a
    /// short-lived, ACL-restricted temp file).
    /// </summary>
    public string Reveal() => _value;

    public int Length => _value.Length;

    /// <summary>
    /// Deliberately does not expose the secret. Always returns a redaction
    /// marker so that accidental logging (string interpolation, ToString()
    /// calls in diagnostics, etc.) never leaks key material.
    /// </summary>
    public override string ToString() => "***REDACTED***";
}
