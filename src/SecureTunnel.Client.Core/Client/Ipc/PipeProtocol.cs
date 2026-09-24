namespace SecureTunnel.Client.Core.Client.Ipc;

/// <summary>
/// Constants shared by both ends of the Named Pipe transport. The pipe
/// name is fixed and product-owned - it is never supplied by a caller.
/// See docs/ipc-protocol.md for the full wire protocol.
/// </summary>
public static class PipeProtocol
{
    /// <summary>
    /// Base pipe name (without the "\\.\pipe\" prefix, which
    /// <see cref="System.IO.Pipes.NamedPipeServerStream"/>/<see cref="System.IO.Pipes.NamedPipeClientStream"/>
    /// add automatically). Versioned so a future breaking protocol change
    /// can introduce a new pipe name rather than silently talking past a
    /// mismatched client/service.
    /// </summary>
    public const string PipeName = "SecureTunnelClientAgent.v1";

    /// <summary>Maximum allowed frame body size, enforced before any read allocation.</summary>
    public const int MaxMessageSizeBytes = 64 * 1024;

    public const int ServerReadWriteTimeoutMs = 5_000;
    public const int ServerOperationTimeoutMs = 30_000;

    public const int ClientConnectTimeoutMs = 10_000;
    public const int ClientCallTimeoutMs = 35_000;

    public static readonly IReadOnlyList<string> AllowedOperations =
        ["Connect", "Disconnect", "Status", "ValidateConfiguration"];
}
