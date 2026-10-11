using SecureTunnel.Client.Core.Client.Ipc;

namespace SecureTunnel.Client.Infrastructure.Tests.Ipc;

/// <summary>
/// These tests host their own server on the product's fixed pipe name. When a real SecureTunnelAgent is already
/// running on this machine it owns that name, so the test is skipped instead of hanging.
/// </summary>
public sealed class PipeFactAttribute : FactAttribute
{
    public PipeFactAttribute()
    {
        if (Directory.GetFiles(@"\\.\pipe\").Any(p => p.EndsWith(PipeProtocol.PipeName, StringComparison.OrdinalIgnoreCase)))
        {
            Skip = "The SecureTunnelAgent service is running and owns the pipe name; stop it to run this test.";
        }
    }
}
