namespace SecureTunnel.Client.Agent.Contracts;

/// <summary>
/// The complete set of privileged operations exposed across the
/// WPF-to-service boundary: Connect, Disconnect, Status, and
/// ValidateConfiguration. There is intentionally no generic
/// "HandleAsync(object request)" catch-all method - every operation the
/// privileged side can perform must have an explicit, narrowly-typed
/// method here. See docs/windows-service-boundary.md for the rationale.
/// </summary>
public interface IPrivilegedOperationHandler
{
    Task<PrivilegedResponse> HandleConnectAsync(ConnectRequest request, CancellationToken cancellationToken);

    Task<PrivilegedResponse> HandleDisconnectAsync(DisconnectRequest request, CancellationToken cancellationToken);

    Task<PrivilegedResponse> HandleStatusAsync(StatusRequest request, CancellationToken cancellationToken);

    Task<PrivilegedResponse> HandleValidateConfigurationAsync(ValidateConfigurationRequest request, CancellationToken cancellationToken);
}
