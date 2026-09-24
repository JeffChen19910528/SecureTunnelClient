namespace SecureTunnel.Client.Core.Client.Ipc;

/// <summary>
/// Wire-level request envelope. <see cref="Operation"/> must be one of
/// <see cref="PipeProtocol.AllowedOperations"/> - the server rejects
/// anything else as <c>InvalidRequest</c> rather than executing it. This
/// envelope deliberately has no field for a raw private key or preshared
/// key; <see cref="RawConfigText"/> is only ever used for
/// <c>ValidateConfiguration</c>, and even then the key material inside it
/// is wrapped in <c>SensitiveString</c> the moment it's parsed server-side
/// and is never echoed back.
/// </summary>
public sealed record PipeRequestEnvelope(string Operation, string ClientId, string? RawConfigText);
