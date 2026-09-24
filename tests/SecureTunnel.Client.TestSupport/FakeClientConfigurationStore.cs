using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.TestSupport;

/// <summary>
/// Test double - not a real implementation. In-memory
/// <see cref="IClientConfigurationStore"/> for unit tests that do not need
/// real DPAPI-backed persistence.
/// </summary>
public sealed class FakeClientConfigurationStore : IClientConfigurationStore
{
    private readonly Dictionary<string, (ClientConfiguration Config, SensitiveString Key)> _entries = new();

    public bool ForceCorrupted { get; set; }

    public Task<StoreResult> SaveAsync(ClientConfiguration configuration, SensitiveString privateKey, CancellationToken cancellationToken)
    {
        _entries[configuration.ClientId] = (configuration, privateKey);
        return Task.FromResult(StoreResult.Ok());
    }

    public Task<LoadResult> LoadAsync(string clientId, CancellationToken cancellationToken)
    {
        if (ForceCorrupted)
        {
            return Task.FromResult(LoadResult.Fail("CorruptedData", "Stored configuration could not be read."));
        }

        if (!_entries.TryGetValue(clientId, out var entry))
        {
            return Task.FromResult(LoadResult.Fail("NotFound", "No stored configuration for this client."));
        }

        return Task.FromResult(LoadResult.Ok(entry.Config, entry.Key));
    }

    public Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.Remove(clientId));
}
