namespace SecureTunnel.Client.Core.Validation;

/// <summary>
/// A single structured validation finding. Never include secret values
/// (private keys) in <see cref="Message"/> or <see cref="FixHint"/>.
/// </summary>
public sealed record ValidationIssue(
    string Field,
    ValidationSeverity Severity,
    string Message,
    string? FixHint = null);
