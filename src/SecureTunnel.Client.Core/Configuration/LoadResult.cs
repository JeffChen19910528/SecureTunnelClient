using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;

namespace SecureTunnel.Client.Core.Configuration;

/// <summary>
/// Result of a configuration store read operation. On failure (including
/// corrupted or tampered storage), the store must fail closed: <see cref="Success"/>
/// is false and no partial/corrupted configuration is returned.
/// <see cref="Message"/> is always user-safe and never contains key material.
/// </summary>
public sealed class LoadResult
{
    public required bool Success { get; init; }

    public ClientConfiguration? Configuration { get; init; }

    public SensitiveString? PrivateKey { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public static LoadResult Ok(ClientConfiguration configuration, SensitiveString privateKey) =>
        new() { Success = true, Configuration = configuration, PrivateKey = privateKey };

    public static LoadResult Fail(string errorCode, string message) =>
        new() { Success = false, ErrorCode = errorCode, Message = message };
}
