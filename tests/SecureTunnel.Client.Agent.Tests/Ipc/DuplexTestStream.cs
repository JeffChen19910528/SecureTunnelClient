namespace SecureTunnel.Client.Agent.Tests.Ipc;

/// <summary>
/// Test double - not a real pipe. Reads come from a pre-filled buffer
/// (simulating what a client sent); writes are captured into a separate
/// buffer (simulating what the server sent back), so a single
/// <see cref="PipeSessionHandler"/>-style read-then-write exchange can be
/// tested without a real Named Pipe.
/// </summary>
internal sealed class DuplexTestStream : Stream
{
    private readonly MemoryStream _incoming;
    public MemoryStream Outgoing { get; } = new();

    public DuplexTestStream(byte[] incomingBytes)
    {
        _incoming = new MemoryStream(incomingBytes);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => _incoming.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _incoming.ReadAsync(buffer, offset, count, cancellationToken);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _incoming.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => Outgoing.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Outgoing.WriteAsync(buffer, offset, count, cancellationToken);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        Outgoing.WriteAsync(buffer, cancellationToken);

    public override void Flush() => Outgoing.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => Outgoing.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
