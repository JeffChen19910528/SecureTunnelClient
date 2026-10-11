using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;
using CoreValidation = SecureTunnel.Client.Core.Validation;

namespace SecureTunnel.Client.Infrastructure.WireGuard.Embedded;

/// <summary>
/// <see cref="IWireGuardClientService"/> that drives the bundled wireguard-nt engine in-process, so the
/// machine does not need WireGuard for Windows (or any other VPN software) installed. As with the
/// wireguard.exe based service, "Connected" is only ever reported from <see cref="GetStatusAsync"/> once
/// a peer handshake has actually been observed; a successful <see cref="ConnectAsync"/> only means the
/// adapter was created and configured.
/// </summary>
public sealed class EmbeddedWireGuardClientService : IWireGuardClientService
{
    /// <summary>A handshake older than this (with keepalive, sessions rekey every ~2 min) is treated as stale.</summary>
    public static readonly TimeSpan HandshakeFreshness = TimeSpan.FromMinutes(3);

    private readonly IWireGuardNativeTunnel _tunnel;
    private readonly IClientConfigurationStore _store;
    private readonly WireGuardClientService _importAndValidate;
    private readonly Func<DateTime> _utcNow;

    public EmbeddedWireGuardClientService(
        IWireGuardNativeTunnel tunnel,
        IClientConfigurationParser parser,
        IClientConfigurationStore store,
        IProcessRunner processRunner,
        Func<DateTime>? utcNow = null)
    {
        _tunnel = tunnel;
        _store = store;
        _importAndValidate = new WireGuardClientService(processRunner, parser, store);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public Task<ImportResult> ImportConfiguration(string rawConfigText, CancellationToken cancellationToken) =>
        _importAndValidate.ImportConfiguration(rawConfigText, cancellationToken);

    public Task<CoreValidation.ValidationResult> ValidateConfiguration(ClientConfiguration configuration, CancellationToken cancellationToken) =>
        _importAndValidate.ValidateConfiguration(configuration, cancellationToken);

    public async Task<ConnectResult> ConnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!_tunnel.IsAvailable(out _))
        {
            return new ConnectResult
            {
                Outcome = WireGuardOutcome.WireGuardNotInstalled,
                Success = false,
                ErrorCode = "EngineUnavailable",
                UserSafeMessage = "The built-in WireGuard engine is missing or unsupported on this computer."
            };
        }

        var loaded = await _store.LoadAsync(configuration.ClientId, cancellationToken);
        if (!loaded.Success || loaded.PrivateKey is null || loaded.Configuration is null)
        {
            return new ConnectResult
            {
                Outcome = WireGuardOutcome.InvalidConfiguration,
                Success = false,
                ErrorCode = loaded.ErrorCode,
                UserSafeMessage = "Stored configuration could not be loaded."
            };
        }

        var name = WireGuardNtConfigBuilder.AdapterNameFor(configuration.ClientId);
        var result = await Task.Run(() => _tunnel.Up(name, loaded.Configuration, loaded.PrivateKey, loaded.Configuration.PresharedKey), cancellationToken);
        if (result.Success)
        {
            return new ConnectResult { Outcome = WireGuardOutcome.Unknown, Success = true, UserSafeMessage = "Tunnel started; awaiting status confirmation." };
        }

        return result.ErrorCode switch
        {
            "PermissionDenied" => new ConnectResult { Outcome = WireGuardOutcome.PermissionDenied, Success = false, ErrorCode = "PermissionDenied", UserSafeMessage = "Administrator permission is required to connect." },
            "InvalidConfiguration" or "FullTunnelUnsupported" => new ConnectResult { Outcome = WireGuardOutcome.InvalidConfiguration, Success = false, ErrorCode = result.ErrorCode, UserSafeMessage = result.Detail ?? "The configuration is not supported." },
            _ => new ConnectResult { Outcome = WireGuardOutcome.ConnectionFailed, Success = false, ErrorCode = result.ErrorCode ?? "ConnectionFailed", UserSafeMessage = "Failed to start the tunnel." },
        };
    }

    public async Task<DisconnectResult> DisconnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        var name = WireGuardNtConfigBuilder.AdapterNameFor(configuration.ClientId);
        var result = await Task.Run(() => _tunnel.Down(name), cancellationToken); // idempotent: absent adapter is success
        return result.Success
            ? new DisconnectResult { Outcome = WireGuardOutcome.Disconnected, Success = true }
            : new DisconnectResult { Outcome = WireGuardOutcome.DisconnectFailed, Success = false, ErrorCode = "DisconnectFailed", UserSafeMessage = "Failed to stop the tunnel." };
    }

    public Task<StatusResult> GetStatusAsync(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!_tunnel.IsAvailable(out _))
        {
            return Task.FromResult(new StatusResult
            {
                Outcome = WireGuardOutcome.WireGuardNotInstalled,
                Success = false,
                ObservedState = ClientState.Error,
                ErrorCode = "EngineUnavailable",
                UserSafeMessage = "The built-in WireGuard engine is missing or unsupported on this computer."
            });
        }

        var snapshot = _tunnel.Query(WireGuardNtConfigBuilder.AdapterNameFor(configuration.ClientId));
        var outcome = snapshot switch
        {
            null => WireGuardOutcome.Disconnected,
            { LastHandshakeUtc: null } => WireGuardOutcome.AwaitingHandshake,
            { LastHandshakeUtc: { } hs } when _utcNow() - hs <= HandshakeFreshness => WireGuardOutcome.Connected,
            _ => WireGuardOutcome.AwaitingHandshake,
        };

        var state = outcome switch
        {
            WireGuardOutcome.Connected => ClientState.Connected,
            WireGuardOutcome.AwaitingHandshake => ClientState.AwaitingHandshake,
            _ => ClientState.Disconnected,
        };

        return Task.FromResult(new StatusResult { Outcome = outcome, Success = true, ObservedState = state });
    }
}
