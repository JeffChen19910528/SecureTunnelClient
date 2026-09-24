using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.Core.Configuration;

/// <summary>
/// Secure local storage for a client configuration and its private key.
/// Implementations must encrypt key material at rest, use atomic writes,
/// and fail closed on corruption rather than silently replacing valid data
/// with corrupted data. Storage paths must not be configurable to
/// arbitrary unsafe locations without validation. No cloud synchronization.
/// </summary>
public interface IClientConfigurationStore
{
    Task<StoreResult> SaveAsync(ClientConfiguration configuration, SensitiveString privateKey, CancellationToken cancellationToken);

    Task<LoadResult> LoadAsync(string clientId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken);
}
