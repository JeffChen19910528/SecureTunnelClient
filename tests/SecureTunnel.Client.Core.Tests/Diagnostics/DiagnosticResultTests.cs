using SecureTunnel.Client.Core.Diagnostics;

namespace SecureTunnel.Client.Core.Tests.Diagnostics;

public class DiagnosticResultTests
{
    [Fact]
    public void TechnicalDetails_NeverContainsPrivateKey_WhenBuiltFromException()
    {
        const string leakedKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var exception = new InvalidOperationException($"Failed to parse key {leakedKey} in config");

        var result = DiagnosticResultFactory.FromException(
            operation: "ImportConfiguration",
            exception: exception,
            errorCode: "InvalidConfiguration",
            userSafeMessage: "The provided configuration is invalid.",
            isRetryable: false);

        Assert.DoesNotContain(leakedKey, result.TechnicalDetails);
        Assert.Contains("***REDACTED***", result.TechnicalDetails);
    }
}
