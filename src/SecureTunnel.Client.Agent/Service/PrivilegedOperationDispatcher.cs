using SecureTunnel.Client.Agent.Contracts;

namespace SecureTunnel.Client.Agent.Service;

/// <summary>
/// Single choke point that routes an incoming <see cref="PrivilegedRequest"/>
/// to the matching <see cref="IPrivilegedOperationHandler"/> method. This
/// is a closed switch over the known request subtypes - there is no
/// reflection-based or string-keyed dispatch that could be extended to run
/// an arbitrary operation, and an unrecognized request type is rejected
/// rather than executed.
/// </summary>
public sealed class PrivilegedOperationDispatcher : IPrivilegedOperationDispatcher
{
    private readonly IPrivilegedOperationHandler _handler;

    public PrivilegedOperationDispatcher(IPrivilegedOperationHandler handler)
    {
        _handler = handler;
    }

    public Task<PrivilegedResponse> DispatchAsync(PrivilegedRequest request, CancellationToken cancellationToken) => request switch
    {
        ConnectRequest connect => _handler.HandleConnectAsync(connect, cancellationToken),
        DisconnectRequest disconnect => _handler.HandleDisconnectAsync(disconnect, cancellationToken),
        StatusRequest status => _handler.HandleStatusAsync(status, cancellationToken),
        ValidateConfigurationRequest validate => _handler.HandleValidateConfigurationAsync(validate, cancellationToken),
        _ => throw new NotSupportedException($"Request type '{request.GetType().Name}' is not an allow-listed privileged operation.")
    };
}
