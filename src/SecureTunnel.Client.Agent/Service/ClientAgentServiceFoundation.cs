using SecureTunnel.Client.Agent.Contracts;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Agent.Service;

/// <summary>
/// The privileged operation boundary implementation. This is where requests from the desktop app reach the
/// WireGuard engine and the encrypted configuration store. It runs inside the elevated Windows Service
/// (LocalSystem), reached only via the Named Pipe transport.
///
/// Security boundary: the desktop app never constructs this type, and never calls
/// <see cref="IWireGuardClientService"/> or <see cref="IClientConfigurationStore"/> itself. All privileged
/// access flows through the methods on <see cref="IPrivilegedOperationHandler"/>.
/// </summary>
public sealed class ClientAgentServiceFoundation : IPrivilegedOperationHandler
{
    private readonly IWireGuardClientService _wireGuardClientService;
    private readonly IClientConfigurationStore _configurationStore;
    private readonly IClientConfigurationParser _configurationParser;

    public ClientAgentServiceFoundation(
        IWireGuardClientService wireGuardClientService,
        IClientConfigurationStore configurationStore,
        IClientConfigurationParser configurationParser)
    {
        _wireGuardClientService = wireGuardClientService;
        _configurationStore = configurationStore;
        _configurationParser = configurationParser;
    }

    public async Task<PrivilegedResponse> HandleConnectAsync(ConnectRequest request, CancellationToken cancellationToken)
    {
        var loaded = await _configurationStore.LoadAsync(request.ClientId, cancellationToken);
        if (!loaded.Success || loaded.Configuration is null)
        {
            return new PrivilegedResponse(false, WireGuardOutcome.InvalidConfiguration, loaded.ErrorCode, loaded.Message, null);
        }

        var result = await _wireGuardClientService.ConnectAsync(loaded.Configuration, cancellationToken);
        return new PrivilegedResponse(result.Success, result.Outcome, result.ErrorCode, result.UserSafeMessage, null);
    }

    public async Task<PrivilegedResponse> HandleDisconnectAsync(DisconnectRequest request, CancellationToken cancellationToken)
    {
        var loaded = await _configurationStore.LoadAsync(request.ClientId, cancellationToken);
        if (!loaded.Success || loaded.Configuration is null)
        {
            return new PrivilegedResponse(false, WireGuardOutcome.InvalidConfiguration, loaded.ErrorCode, loaded.Message, null);
        }

        var result = await _wireGuardClientService.DisconnectAsync(loaded.Configuration, cancellationToken);
        return new PrivilegedResponse(result.Success, result.Outcome, result.ErrorCode, result.UserSafeMessage, null);
    }

    public async Task<PrivilegedResponse> HandleStatusAsync(StatusRequest request, CancellationToken cancellationToken)
    {
        var loaded = await _configurationStore.LoadAsync(request.ClientId, cancellationToken);
        if (!loaded.Success || loaded.Configuration is null)
        {
            return new PrivilegedResponse(false, WireGuardOutcome.InvalidConfiguration, loaded.ErrorCode, loaded.Message, null);
        }

        var result = await _wireGuardClientService.GetStatusAsync(loaded.Configuration, cancellationToken);
        return new PrivilegedResponse(result.Success, result.Outcome, result.ErrorCode, result.UserSafeMessage, result.ObservedState);
    }

    /// <summary>
    /// Parses and validates the supplied configuration text; on success,
    /// persists it via the secure store (mirroring
    /// <see cref="IWireGuardClientService.ImportConfiguration"/>) and
    /// returns a <see cref="ConfigurationSummary"/> the WPF import flow
    /// can display and use as the newly-selected client's id. Never
    /// returns the private or preshared key.
    /// </summary>
    public async Task<PrivilegedResponse> HandleValidateConfigurationAsync(ValidateConfigurationRequest request, CancellationToken cancellationToken)
    {
        var parsed = _configurationParser.Parse(request.RawConfigText);

        if (!parsed.Success || parsed.Configuration is null || parsed.PrivateKey is null)
        {
            return new PrivilegedResponse(
                false,
                WireGuardOutcome.InvalidConfiguration,
                "InvalidConfiguration",
                "The provided configuration is invalid.",
                null);
        }

        var stored = await _configurationStore.SaveAsync(parsed.Configuration, parsed.PrivateKey, cancellationToken);
        if (!stored.Success)
        {
            return new PrivilegedResponse(false, WireGuardOutcome.Unknown, stored.ErrorCode, stored.Message, null);
        }

        var summary = new ConfigurationSummary(
            parsed.Configuration.ClientId,
            parsed.Configuration.GatewayEndpoint,
            parsed.Configuration.InterfaceAddress,
            parsed.Configuration.AllowedIPs);

        return new PrivilegedResponse(true, WireGuardOutcome.Unknown, null, null, null, summary);
    }
}
