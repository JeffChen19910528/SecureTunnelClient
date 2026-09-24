using SecureTunnel.Client.Agent.Contracts;

namespace SecureTunnel.Client.Agent.Service;

public interface IPrivilegedOperationDispatcher
{
    Task<PrivilegedResponse> DispatchAsync(PrivilegedRequest request, CancellationToken cancellationToken);
}
