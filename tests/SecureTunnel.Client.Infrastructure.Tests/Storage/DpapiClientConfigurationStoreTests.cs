using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.Infrastructure.Storage;

namespace SecureTunnel.Client.Infrastructure.Tests.Storage;

public class DpapiClientConfigurationStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public DpapiClientConfigurationStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"stc-tests-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static ClientConfiguration SampleConfiguration(string clientId) => new()
    {
        ClientId = clientId,
        GatewayName = "Test Gateway",
        GatewayEndpoint = "gateway.example.com:51820",
        InterfaceAddress = "10.0.0.2/32",
        PeerPublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=",
        PeerEndpoint = "gateway.example.com:51820",
        AllowedIPs = ["10.10.0.0/24"],
        ConfigurationSource = ConfigurationSource.Imported,
        ImportedAtUtc = DateTime.UtcNow,
        Status = ConfigurationStatus.Valid
    };

    [Fact]
    public async Task SaveAsync_EncryptsPrivateKeyAtRest()
    {
        var store = new DpapiClientConfigurationStore(_tempRoot);
        var clientId = Guid.NewGuid().ToString("N");
        var privateKey = new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        var saveResult = await store.SaveAsync(SampleConfiguration(clientId), privateKey, CancellationToken.None);

        Assert.True(saveResult.Success);
        var filePath = Path.Combine(_tempRoot, $"{clientId}.stc");
        var onDiskBytes = File.ReadAllBytes(filePath);
        var onDiskText = System.Text.Encoding.UTF8.GetString(onDiskBytes);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", onDiskText);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsConfiguration()
    {
        var store = new DpapiClientConfigurationStore(_tempRoot);
        var clientId = Guid.NewGuid().ToString("N");
        var privateKey = new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        var configuration = SampleConfiguration(clientId);

        await store.SaveAsync(configuration, privateKey, CancellationToken.None);
        var loadResult = await store.LoadAsync(clientId, CancellationToken.None);

        Assert.True(loadResult.Success);
        Assert.Equal(configuration.ClientId, loadResult.Configuration!.ClientId);
        Assert.Equal(configuration.InterfaceAddress, loadResult.Configuration.InterfaceAddress);
        Assert.Equal(privateKey.Reveal(), loadResult.PrivateKey!.Reveal());
    }

    [Fact]
    public async Task LoadAsync_CorruptedFile_FailsClosed()
    {
        Directory.CreateDirectory(_tempRoot);
        var clientId = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_tempRoot, $"{clientId}.stc");
        File.WriteAllBytes(filePath, [1, 2, 3, 4, 5]);

        var store = new DpapiClientConfigurationStore(_tempRoot);

        var loadResult = await store.LoadAsync(clientId, CancellationToken.None);

        Assert.False(loadResult.Success);
        Assert.Null(loadResult.Configuration);
        Assert.Null(loadResult.PrivateKey);
    }

    [Fact]
    public async Task SaveAsync_AtomicWriteFailure_DoesNotCorruptExistingFile()
    {
        var failingWriter = new ThrowingAtomicFileWriter();
        var store = new DpapiClientConfigurationStore(_tempRoot, failingWriter);
        var clientId = Guid.NewGuid().ToString("N");
        var privateKey = new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");

        var result = await store.SaveAsync(SampleConfiguration(clientId), privateKey, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(StorageErrorCodes.AtomicWriteFailed, result.ErrorCode);
        Assert.False(File.Exists(Path.Combine(_tempRoot, $"{clientId}.stc")));
    }

    [Fact]
    public async Task LoadAsync_CorruptedFile_ErrorMessageContainsNoKeyMaterial()
    {
        Directory.CreateDirectory(_tempRoot);
        var clientId = Guid.NewGuid().ToString("N");
        var filePath = Path.Combine(_tempRoot, $"{clientId}.stc");
        File.WriteAllBytes(filePath, [9, 9, 9]);

        var store = new DpapiClientConfigurationStore(_tempRoot);

        var loadResult = await store.LoadAsync(clientId, CancellationToken.None);

        Assert.NotNull(loadResult.Message);
        Assert.DoesNotContain("PrivateKey", loadResult.Message);
        Assert.DoesNotContain("=", loadResult.Message);
    }

    [Fact]
    public async Task SaveThenLoad_LocalMachineScope_RoundTripsConfiguration()
    {
        var store = new DpapiClientConfigurationStore(_tempRoot, protectionScope: System.Security.Cryptography.DataProtectionScope.LocalMachine);
        var clientId = Guid.NewGuid().ToString("N");
        var privateKey = new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        var configuration = SampleConfiguration(clientId);

        await store.SaveAsync(configuration, privateKey, CancellationToken.None);
        var loadResult = await store.LoadAsync(clientId, CancellationToken.None);

        Assert.True(loadResult.Success);
        Assert.Equal(privateKey.Reveal(), loadResult.PrivateKey!.Reveal());
    }

    [Fact]
    public void DefaultStorageRoot_ResolvesUnder_ProgramData_SecureTunnel_Agent_Configs()
    {
        // Path-resolution verification for the Phase C6 deployment layout
        // (docs/deployment-guide.md): the default (no storageRoot override)
        // must resolve under %ProgramData%\SecureTunnel\Agent\configs -
        // the machine-wide directory install-service.ps1 provisions with a
        // restricted ACL, never under a user-specific profile directory.
        var store = new DpapiClientConfigurationStore();

        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "SecureTunnel", "Agent", "configs");

        Assert.Equal(Path.GetFullPath(expectedRoot), store.StorageRoot);
    }

    [Fact]
    public void CustomStorageRoot_OutsideAllowedRoots_IsRejected()
    {
        // Deployment-layout safety: an installer/caller cannot point
        // storage at an arbitrary path (e.g. a world-writable temp share
        // or a location outside the machine's application-data tree).
        var arbitraryPath = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "NotAnAllowedLocation");

        Assert.Throws<ArgumentException>(() => new DpapiClientConfigurationStore(arbitraryPath));
    }

    [Fact]
    public async Task LoadAsync_LegacyCurrentUserScopedData_SameIdentity_ActuallyDecryptsSuccessfully()
    {
        // REAL-WINDOWS FINDING (Phase C4 acceptance testing), correcting an
        // assumption documented in Phase C3: Windows DPAPI's CryptUnprotectData
        // determines which master key (user or machine) to use from metadata
        // embedded in the blob itself, not solely from the scope flag passed
        // to Unprotect. When the *same Windows identity* that encrypted a
        // CurrentUser-scoped blob later calls Unprotect with LocalMachine
        // scope, decryption still succeeds - the scope flag mismatch alone
        // does NOT make legacy data unreadable for that identity. This was
        // verified on this real Windows 11 machine (not assumed/mocked).
        // See docs/security-model.md and docs/roadmap.md for what this does
        // and does not change about the documented migration risk (a
        // genuinely different identity, e.g. LocalSystem, still cannot
        // decrypt another user's CurrentUser-scoped profile data - that
        // remains untested here since no elevated LocalSystem process was
        // available in this environment; see docs/windows-service-installation.md).
        var legacyStore = new DpapiClientConfigurationStore(_tempRoot, protectionScope: System.Security.Cryptography.DataProtectionScope.CurrentUser);
        var clientId = Guid.NewGuid().ToString("N");
        var privateKey = new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        await legacyStore.SaveAsync(SampleConfiguration(clientId), privateKey, CancellationToken.None);

        var currentStore = new DpapiClientConfigurationStore(_tempRoot, protectionScope: System.Security.Cryptography.DataProtectionScope.LocalMachine);
        var loadResult = await currentStore.LoadAsync(clientId, CancellationToken.None);

        Assert.True(loadResult.Success);
        Assert.Equal(privateKey.Reveal(), loadResult.PrivateKey!.Reveal());
    }

    private sealed class ThrowingAtomicFileWriter : IAtomicFileWriter
    {
        public void WriteAllBytesAtomic(string path, byte[] data) => throw new IOException("Simulated atomic write failure.");
        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
        public bool Exists(string path) => File.Exists(path);
    }
}
