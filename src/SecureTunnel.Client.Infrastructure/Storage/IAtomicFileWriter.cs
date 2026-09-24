namespace SecureTunnel.Client.Infrastructure.Storage;

/// <summary>
/// Isolates the atomic write/read mechanics from <see cref="DpapiClientConfigurationStore"/>
/// so storage-failure scenarios (e.g. a failed atomic write) can be
/// exercised in tests without touching the real filesystem.
/// </summary>
public interface IAtomicFileWriter
{
    /// <summary>
    /// Writes <paramref name="data"/> to <paramref name="path"/> such that
    /// readers never observe a partially-written file: implementations
    /// write to a temp file first and then perform an atomic rename.
    /// </summary>
    void WriteAllBytesAtomic(string path, byte[] data);

    byte[] ReadAllBytes(string path);

    bool Exists(string path);
}
