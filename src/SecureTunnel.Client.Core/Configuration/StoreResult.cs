namespace SecureTunnel.Client.Core.Configuration;

/// <summary>
/// Result of a configuration store write operation. <see cref="Message"/>
/// is always a user-safe string; raw exception text and key material must
/// never appear here.
/// </summary>
public sealed class StoreResult
{
    public required bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public static StoreResult Ok() => new() { Success = true };

    public static StoreResult Fail(string errorCode, string message) =>
        new() { Success = false, ErrorCode = errorCode, Message = message };
}
