namespace SecureTunnel.Client.Infrastructure.Storage;

/// <summary>
/// Real filesystem implementation of <see cref="IAtomicFileWriter"/>: write
/// to a sibling temp file, flush, then rename over the destination so a
/// reader never observes a half-written file.
/// </summary>
public sealed class AtomicFileWriter : IAtomicFileWriter
{
    public void WriteAllBytesAtomic(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Storage path must include a directory.");
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        File.WriteAllBytes(tempPath, data);
        File.Move(tempPath, path, overwrite: true);
    }

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public bool Exists(string path) => File.Exists(path);
}
