using System.IO.Pipes;
using System.Text.Json;
using SecureTunnel.Client.Core.Client;
using SecureTunnel.Client.Core.Client.Ipc;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Infrastructure.Ipc;

/// <summary>
/// WPF-facing <see cref="IPrivilegedClientService"/> implementation backed
/// by the Named Pipe transport. Opens a fresh <see cref="NamedPipeClientStream"/>
/// per call (no long-lived connection held across calls) so a service
/// restart between calls is handled naturally - the next call simply
/// reconnects. Never sends or expects a private key or preshared key
/// anywhere in the request/response envelope.
/// </summary>
public sealed class NamedPipePrivilegedClientProxy : IPrivilegedClientService
{
    public Task<PrivilegedClientResult> ValidateConfigurationAsync(string rawConfigText, CancellationToken cancellationToken) =>
        SendAsync(new PipeRequestEnvelope("ValidateConfiguration", ClientId: string.Empty, rawConfigText), cancellationToken);

    public Task<PrivilegedClientResult> ConnectAsync(string clientId, CancellationToken cancellationToken) =>
        SendAsync(new PipeRequestEnvelope("Connect", clientId, RawConfigText: null), cancellationToken);

    public Task<PrivilegedClientResult> DisconnectAsync(string clientId, CancellationToken cancellationToken) =>
        SendAsync(new PipeRequestEnvelope("Disconnect", clientId, RawConfigText: null), cancellationToken);

    public Task<PrivilegedClientResult> GetStatusAsync(string clientId, CancellationToken cancellationToken) =>
        SendAsync(new PipeRequestEnvelope("Status", clientId, RawConfigText: null), cancellationToken);

    private static async Task<PrivilegedClientResult> SendAsync(PipeRequestEnvelope request, CancellationToken cancellationToken)
    {
        using var callCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        callCts.CancelAfter(TimeSpan.FromMilliseconds(PipeProtocol.ClientCallTimeoutMs));

        await using var client = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await client.ConnectAsync(PipeProtocol.ClientConnectTimeoutMs, callCts.Token);
        }
        catch (TimeoutException)
        {
            return Unavailable("The SecureTunnel Connect service is not responding.");
        }
        catch (System.IO.FileNotFoundException)
        {
            return Unavailable("The SecureTunnel Connect service is not running.");
        }
        catch (UnauthorizedAccessException)
        {
            return new PrivilegedClientResult(false, WireGuardOutcome.AccessDenied, "AccessDenied", "Access to the SecureTunnel Connect service was denied.", null);
        }

        try
        {
            var requestJson = JsonSerializer.Serialize(request, PipeJsonOptions.Default);
            await PipeFraming.WriteFrameAsync(client, requestJson, callCts.Token);

            var responseJson = await PipeFraming.ReadFrameAsync(client, callCts.Token);
            var response = JsonSerializer.Deserialize<PipeResponseEnvelope>(responseJson, PipeJsonOptions.Default);

            if (response is null)
            {
                return new PrivilegedClientResult(false, WireGuardOutcome.InvalidRequest, "InvalidResponse", "The service returned an unreadable response.", null);
            }

            ConfigurationSummary? configuration = response.ConfigClientId is not null
                ? new ConfigurationSummary(
                    response.ConfigClientId,
                    response.ConfigGatewayEndpoint ?? string.Empty,
                    response.ConfigInterfaceAddress ?? string.Empty,
                    response.ConfigAllowedIPs ?? [])
                : null;

            return new PrivilegedClientResult(
                response.Success,
                Enum.TryParse<WireGuardOutcome>(response.Outcome, out var outcome) ? outcome : WireGuardOutcome.Unknown,
                response.ErrorCode,
                response.UserSafeMessage,
                response.ObservedState is not null && Enum.TryParse<ClientState>(response.ObservedState, out var state) ? state : null,
                configuration);
        }
        catch (OperationCanceledException)
        {
            return new PrivilegedClientResult(false, WireGuardOutcome.PipeConnectionFailed, "Timeout", "The request to the SecureTunnel Connect service timed out.", null);
        }
        catch (IOException)
        {
            return new PrivilegedClientResult(false, WireGuardOutcome.PipeConnectionFailed, "PipeConnectionFailed", "The connection to the SecureTunnel Connect service was lost.", null);
        }
        catch (JsonException)
        {
            return new PrivilegedClientResult(false, WireGuardOutcome.InvalidRequest, "InvalidResponse", "The service returned an unreadable response.", null);
        }
    }

    private static PrivilegedClientResult Unavailable(string message) =>
        new(false, WireGuardOutcome.ServiceUnavailable, "ServiceUnavailable", message, null);
}
