using SecureTunnel.Client.Core.Client;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.TestSupport;

/// <summary>
/// Test double - not a real implementation. Never opens a real Named
/// Pipe; every operation returns a pre-programmed result.
/// </summary>
public sealed class FakePrivilegedClientService : IPrivilegedClientService
{
    public PrivilegedClientResult ValidateConfigurationResult { get; set; } = Ok();
    public PrivilegedClientResult ConnectResult { get; set; } = Ok();
    public PrivilegedClientResult DisconnectResult { get; set; } = Ok();
    public PrivilegedClientResult StatusResult { get; set; } = Ok();

    public List<string> Calls { get; } = [];

    public Task<PrivilegedClientResult> ValidateConfigurationAsync(string rawConfigText, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ValidateConfigurationAsync));
        return Task.FromResult(ValidateConfigurationResult);
    }

    public Task<PrivilegedClientResult> ConnectAsync(string clientId, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(ConnectAsync));
        return Task.FromResult(ConnectResult);
    }

    public Task<PrivilegedClientResult> DisconnectAsync(string clientId, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(DisconnectAsync));
        return Task.FromResult(DisconnectResult);
    }

    public Task<PrivilegedClientResult> GetStatusAsync(string clientId, CancellationToken cancellationToken)
    {
        Calls.Add(nameof(GetStatusAsync));
        return Task.FromResult(StatusResult);
    }

    private static PrivilegedClientResult Ok() => new(true, WireGuardOutcome.Unknown, null, null, null);
}
