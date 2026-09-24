using System.Buffers.Binary;
using System.Text;

namespace SecureTunnel.Client.Core.Client.Ipc;

/// <summary>
/// Shared length-prefixed framing used by both the Named Pipe server and
/// client: a 4-byte little-endian length prefix followed by a UTF-8 JSON
/// body. Enforces <see cref="PipeProtocol.MaxMessageSizeBytes"/> on the
/// length prefix before any body buffer is allocated, so an oversized or
/// malicious length value is rejected cheaply.
/// </summary>
public static class PipeFraming
{
    public static async Task WriteFrameAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        if (bodyBytes.Length > PipeProtocol.MaxMessageSizeBytes)
        {
            throw new InvalidOperationException("Outgoing message exceeds the maximum allowed size.");
        }

        Span<byte> lengthPrefix = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, bodyBytes.Length);

        await stream.WriteAsync(lengthPrefix.ToArray(), cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads one frame. Throws <see cref="PipeMessageTooLargeException"/>
    /// if the declared length exceeds the limit - the caller can then
    /// close the connection without ever allocating an oversized buffer.
    /// </summary>
    public static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthPrefix = new byte[4];
        await ReadExactAsync(stream, lengthPrefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);

        if (length < 0 || length > PipeProtocol.MaxMessageSizeBytes)
        {
            throw new PipeMessageTooLargeException(length);
        }

        var body = new byte[length];
        await ReadExactAsync(stream, body, cancellationToken);

        return Encoding.UTF8.GetString(body);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("Pipe closed before the expected number of bytes were read.");
            }

            offset += read;
        }
    }
}

public sealed class PipeMessageTooLargeException : Exception
{
    public int DeclaredLength { get; }

    public PipeMessageTooLargeException(int declaredLength)
        : base($"Message declares a length of {declaredLength} bytes, exceeding the {PipeProtocol.MaxMessageSizeBytes}-byte limit.")
    {
        DeclaredLength = declaredLength;
    }
}
