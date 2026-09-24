namespace SecureTunnel.Client.Agent.Contracts;

/// <summary>
/// Base type for the fixed, allow-listed set of operations the privileged
/// boundary accepts. There is deliberately no generic/free-form request
/// type here (e.g. no "RunCommand(string)") - the set of concrete
/// subtypes below IS the entire allow-list. Adding a new privileged
/// capability requires adding a new record here and a corresponding
/// handler method on <see cref="IPrivilegedOperationHandler"/>, not a new
/// code path that parses arbitrary input.
/// </summary>
public abstract record PrivilegedRequest(string ClientId);

public sealed record ConnectRequest(string ClientId) : PrivilegedRequest(ClientId);

public sealed record DisconnectRequest(string ClientId) : PrivilegedRequest(ClientId);

public sealed record StatusRequest(string ClientId) : PrivilegedRequest(ClientId);

/// <summary>
/// <see cref="RawConfigText"/> is imported configuration text supplied by
/// the user, not a command - the handler only ever passes it to
/// <see cref="SecureTunnel.Client.Core.Configuration.IClientConfigurationParser"/>.
/// </summary>
public sealed record ValidateConfigurationRequest(string ClientId, string RawConfigText) : PrivilegedRequest(ClientId);
