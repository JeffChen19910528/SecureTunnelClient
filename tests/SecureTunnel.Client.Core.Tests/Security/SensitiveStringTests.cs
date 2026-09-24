using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.TestSupport;

namespace SecureTunnel.Client.Core.Tests.Security;

public class SensitiveStringTests
{
    [Fact]
    public void ToString_NeverExposesRawValue()
    {
        var secret = new SensitiveString("super-secret-private-key-value==");

        var text = secret.ToString();

        Assert.DoesNotContain("super-secret-private-key-value==", text);
        Assert.Equal("***REDACTED***", text);
    }

    [Fact]
    public void Reveal_ReturnsOriginalValue()
    {
        var secret = new SensitiveString("super-secret-private-key-value==");

        Assert.Equal("super-secret-private-key-value==", secret.Reveal());
    }

    [Fact]
    public void PrivateKey_ExcludedFromLogOutput()
    {
        var secret = new SensitiveString("super-secret-private-key-value==");
        var sink = new InMemoryLoggerSink();

        sink.Log($"Imported configuration with key {secret}");

        Assert.False(sink.AnyLineContains("super-secret-private-key-value=="));
        Assert.True(sink.AnyLineContains("***REDACTED***"));
    }
}
