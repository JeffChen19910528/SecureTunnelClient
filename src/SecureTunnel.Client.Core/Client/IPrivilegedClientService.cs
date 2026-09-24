namespace SecureTunnel.Client.Core.Client;

/// <summary>
/// The interface the WPF UI depends on to reach privileged operations.
/// Implementations must reach the privileged boundary only through the
/// Named Pipe transport (see docs/ipc-protocol.md) - the UI itself must
/// never start a process, touch WireGuard, or read/write encrypted
/// configuration storage directly. A DI-swappable fake implementation
/// (<c>FakePrivilegedClientService</c>, in SecureTunnel.Client.TestSupport)
/// is used in unit tests.
/// </summary>
public interface IPrivilegedClientService
{
    Task<PrivilegedClientResult> ValidateConfigurationAsync(string rawConfigText, CancellationToken cancellationToken);

    Task<PrivilegedClientResult> ConnectAsync(string clientId, CancellationToken cancellationToken);

    Task<PrivilegedClientResult> DisconnectAsync(string clientId, CancellationToken cancellationToken);

    Task<PrivilegedClientResult> GetStatusAsync(string clientId, CancellationToken cancellationToken);
}
