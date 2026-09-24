using System.Text.RegularExpressions;

namespace SecureTunnel.Client.Core.Diagnostics;

/// <summary>
/// Builds <see cref="DiagnosticResult"/> instances from exceptions while
/// stripping anything that looks like WireGuard key material (44-character
/// base64 tokens ending in '=') from the technical details before they are
/// ever stored or displayed.
/// </summary>
public static class DiagnosticResultFactory
{
    private static readonly Regex Base64KeyLikeToken = new(@"[A-Za-z0-9+/]{42,44}={0,2}", RegexOptions.Compiled);

    public static DiagnosticResult FromException(
        string operation,
        Exception exception,
        string errorCode,
        string userSafeMessage,
        bool isRetryable)
    {
        var sanitizedDetails = Sanitize(exception.Message);

        return new DiagnosticResult(
            Operation: operation,
            Result: DiagnosticOutcome.Failure,
            ErrorCode: errorCode,
            UserSafeMessage: userSafeMessage,
            TechnicalDetails: sanitizedDetails,
            TimestampUtc: DateTime.UtcNow,
            IsRetryable: isRetryable);
    }

    public static string Sanitize(string rawDetails) =>
        Base64KeyLikeToken.Replace(rawDetails, "***REDACTED***");
}
