using System.Security.Cryptography;
using System.Text.Json;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.Infrastructure.Storage;

/// <summary>
/// Stores client configuration (including the private key) encrypted at
/// rest via Windows DPAPI, scoped to <see cref="DataProtectionScope.LocalMachine"/>.
/// Writes are atomic. On any decryption or deserialization failure this
/// store fails closed: it reports failure and leaves the existing on-disk
/// file untouched rather than risk replacing a valid configuration with
/// corrupted data. Error messages never include key material.
///
/// The machine scope (rather than <see cref="DataProtectionScope.CurrentUser"/>,
/// used through Phase C2) is a deliberate consequence of the Agent being
/// designed to eventually run as LocalSystem (see
/// docs/windows-service-boundary.md's Service Identity Rationale): a
/// CurrentUser-scoped blob encrypted by one identity cannot be decrypted
/// by a different one. This is a breaking storage-format change with no
/// migration path - configurations saved by Phase C1/C2 builds cannot be
/// read by this version and must be re-imported (documented as a
/// Deferred Item; acceptable for pre-1.0 prototype software with no real
/// deployments).
/// </summary>
public sealed class DpapiClientConfigurationStore : IClientConfigurationStore
{
    private readonly string _storageRoot;
    private readonly IAtomicFileWriter _fileWriter;
    private readonly DataProtectionScope _protectionScope;

    /// <summary>The resolved, validated directory this store reads/writes under - exposed for deployment/path-resolution verification (see docs/deployment-guide.md).</summary>
    public string StorageRoot => _storageRoot;

    public DpapiClientConfigurationStore(
        string? storageRoot = null,
        IAtomicFileWriter? fileWriter = null,
        DataProtectionScope protectionScope = DataProtectionScope.LocalMachine)
    {
        var defaultRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SecureTunnel", "Agent", "configs");

        _storageRoot = ValidateStorageRoot(storageRoot, defaultRoot);
        _fileWriter = fileWriter ?? new AtomicFileWriter();
        _protectionScope = protectionScope;
    }

    public Task<StoreResult> SaveAsync(ClientConfiguration configuration, SensitiveString privateKey, CancellationToken cancellationToken)
    {
        try
        {
            var dto = new PersistedConfigurationDto
            {
                ClientId = configuration.ClientId,
                PeerId = configuration.PeerId,
                GatewayName = configuration.GatewayName,
                GatewayEndpoint = configuration.GatewayEndpoint,
                InterfaceAddress = configuration.InterfaceAddress,
                DnsServers = configuration.DnsServers?.ToList(),
                Mtu = configuration.Mtu,
                PeerPublicKey = configuration.PeerPublicKey,
                PeerEndpoint = configuration.PeerEndpoint,
                AllowedIPs = configuration.AllowedIPs.ToList(),
                PersistentKeepalive = configuration.PersistentKeepalive,
                ConfigurationSource = configuration.ConfigurationSource.ToString(),
                ImportedAtUtc = configuration.ImportedAtUtc,
                LastUsedAtUtc = configuration.LastUsedAtUtc,
                Status = configuration.Status.ToString(),
                PrivateKey = privateKey.Reveal(),
                PresharedKey = configuration.PresharedKey?.Reveal()
            };

            var plaintextBytes = JsonSerializer.SerializeToUtf8Bytes(dto);
            var encryptedBytes = ProtectedData.Protect(plaintextBytes, optionalEntropy: null, _protectionScope);

            _fileWriter.WriteAllBytesAtomic(GetPath(configuration.ClientId), encryptedBytes);

            return Task.FromResult(StoreResult.Ok());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(StoreResult.Fail(StorageErrorCodes.AtomicWriteFailed, "Failed to save configuration to secure storage."));
        }
    }

    public Task<LoadResult> LoadAsync(string clientId, CancellationToken cancellationToken)
    {
        var path = GetPath(clientId);

        if (!_fileWriter.Exists(path))
        {
            return Task.FromResult(LoadResult.Fail(StorageErrorCodes.NotFound, "No stored configuration for this client."));
        }

        byte[] encryptedBytes;
        try
        {
            encryptedBytes = _fileWriter.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return Task.FromResult(LoadResult.Fail(StorageErrorCodes.CorruptedData, "Stored configuration could not be read."));
        }

        byte[] plaintextBytes;
        try
        {
            plaintextBytes = ProtectedData.Unprotect(encryptedBytes, optionalEntropy: null, _protectionScope);
        }
        catch (CryptographicException)
        {
            return Task.FromResult(LoadResult.Fail(StorageErrorCodes.DecryptionFailed, "Stored configuration is corrupted and could not be decrypted."));
        }

        PersistedConfigurationDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PersistedConfigurationDto>(plaintextBytes);
        }
        catch (JsonException)
        {
            return Task.FromResult(LoadResult.Fail(StorageErrorCodes.CorruptedData, "Stored configuration is corrupted."));
        }

        if (dto is null)
        {
            return Task.FromResult(LoadResult.Fail(StorageErrorCodes.CorruptedData, "Stored configuration is corrupted."));
        }

        var configuration = new ClientConfiguration
        {
            ClientId = dto.ClientId,
            PeerId = dto.PeerId,
            GatewayName = dto.GatewayName,
            GatewayEndpoint = dto.GatewayEndpoint,
            InterfaceAddress = dto.InterfaceAddress,
            DnsServers = dto.DnsServers,
            Mtu = dto.Mtu,
            PeerPublicKey = dto.PeerPublicKey,
            PeerEndpoint = dto.PeerEndpoint,
            PresharedKey = dto.PresharedKey is null ? null : new SensitiveString(dto.PresharedKey),
            AllowedIPs = dto.AllowedIPs,
            PersistentKeepalive = dto.PersistentKeepalive,
            ConfigurationSource = Enum.Parse<ConfigurationSource>(dto.ConfigurationSource),
            ImportedAtUtc = dto.ImportedAtUtc,
            LastUsedAtUtc = dto.LastUsedAtUtc,
            Status = Enum.Parse<ConfigurationStatus>(dto.Status)
        };

        return Task.FromResult(LoadResult.Ok(configuration, new SensitiveString(dto.PrivateKey)));
    }

    public Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken)
    {
        var path = GetPath(clientId);
        if (!_fileWriter.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    private string GetPath(string clientId) => Path.Combine(_storageRoot, $"{clientId}.stc");

    private static string ValidateStorageRoot(string? requestedRoot, string defaultRoot)
    {
        if (requestedRoot is null)
        {
            return defaultRoot;
        }

        var fullPath = Path.GetFullPath(requestedRoot);
        var localAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var commonAppData = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        var tempPath = Path.GetFullPath(Path.GetTempPath());

        var isUnderAllowedRoot = fullPath.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(commonAppData, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase);

        if (!isUnderAllowedRoot)
        {
            throw new ArgumentException(
                "Storage path must be under the machine-wide application data directory, the current user's local application data, or the temp directory.",
                nameof(requestedRoot));
        }

        return fullPath;
    }
}
