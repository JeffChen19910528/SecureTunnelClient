namespace SecureTunnel.Client.Core.Diagnostics;

/// <summary>
/// A structured, user-facing diagnostic entry. <see cref="TechnicalDetails"/>
/// must never contain secret material (private keys) or unsanitized raw
/// process output - only sanitized technical context safe to display or
/// export for support purposes.
/// </summary>
public sealed record DiagnosticResult(
    string Operation,
    DiagnosticOutcome Result,
    string ErrorCode,
    string UserSafeMessage,
    string TechnicalDetails,
    DateTime TimestampUtc,
    bool IsRetryable);
