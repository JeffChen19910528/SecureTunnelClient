using System.Diagnostics;
using System.Text.Json;
using SecureTunnel.Client.Agent.Contracts;
using SecureTunnel.Client.Agent.Service;
using SecureTunnel.Client.Core.Client.Ipc;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Agent.Ipc;

/// <summary>
/// Handles a single Named Pipe session end-to-end: read one framed
/// request, validate it, dispatch it, write one framed response. Never
/// throws an unhandled exception back into the caller's connection-accept
/// loop - every failure mode (malformed JSON, oversized message, unknown
/// operation, I/O error, timeout) is converted into a structured
/// <see cref="PipeResponseEnvelope"/> where possible, or a clean
/// connection close otherwise. Never forwards a request to anything other
/// than the four allow-listed <see cref="IPrivilegedOperationDispatcher"/>
/// operations - there is no generic command path here.
/// </summary>
public sealed class PipeSessionHandler
{
    private readonly IPrivilegedOperationDispatcher _dispatcher;

    public PipeSessionHandler(IPrivilegedOperationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task HandleAsync(Stream pipeStream, CancellationToken cancellationToken)
    {
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCts.CancelAfter(TimeSpan.FromMilliseconds(PipeProtocol.ServerOperationTimeoutMs));

        PipeResponseEnvelope response;

        try
        {
            var requestJson = await PipeFraming.ReadFrameAsync(pipeStream, operationCts.Token);
            response = await HandleRequestJsonAsync(requestJson, operationCts.Token);
        }
        catch (PipeMessageTooLargeException)
        {
            response = ErrorResponse(WireGuardOutcome.InvalidRequest, "InvalidRequest", "The request exceeds the maximum allowed message size.");
        }
        catch (JsonException)
        {
            response = ErrorResponse(WireGuardOutcome.InvalidRequest, "InvalidRequest", "The request could not be parsed.");
        }
        catch (OperationCanceledException)
        {
            response = ErrorResponse(WireGuardOutcome.PipeConnectionFailed, "Timeout", "The request timed out.");
        }
        catch (IOException)
        {
            // Client disconnected mid-request; nothing to write back to.
            return;
        }

        try
        {
            var responseJson = JsonSerializer.Serialize(response, PipeJsonOptions.Default);
            await PipeFraming.WriteFrameAsync(pipeStream, responseJson, cancellationToken);
        }
        catch (IOException)
        {
            // Client disconnected before the response could be delivered.
        }
    }

    private async Task<PipeResponseEnvelope> HandleRequestJsonAsync(string requestJson, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<PipeRequestEnvelope>(requestJson, PipeJsonOptions.Default);
        if (envelope is null)
        {
            return ErrorResponse(WireGuardOutcome.InvalidRequest, "InvalidRequest", "The request could not be parsed.");
        }

        if (!PipeProtocol.AllowedOperations.Contains(envelope.Operation))
        {
            return ErrorResponse(WireGuardOutcome.InvalidRequest, "InvalidRequest", "The requested operation is not supported.");
        }

        // ValidateConfiguration is the one operation that legitimately has
        // no ClientId yet - it is how a new configuration's id is
        // assigned in the first place (see the WPF import flow).
        if (envelope.Operation != "ValidateConfiguration" && string.IsNullOrWhiteSpace(envelope.ClientId))
        {
            return ErrorResponse(WireGuardOutcome.InvalidRequest, "InvalidRequest", "A client id is required.");
        }

        PrivilegedRequest request = envelope.Operation switch
        {
            "Connect" => new ConnectRequest(envelope.ClientId),
            "Disconnect" => new DisconnectRequest(envelope.ClientId),
            "Status" => new StatusRequest(envelope.ClientId),
            "ValidateConfiguration" => new ValidateConfigurationRequest(envelope.ClientId, envelope.RawConfigText ?? string.Empty),
            _ => throw new UnreachableException($"Operation '{envelope.Operation}' passed the allow-list check but has no mapping.")
        };

        var response = await _dispatcher.DispatchAsync(request, cancellationToken);

        return new PipeResponseEnvelope(
            response.Success,
            response.Outcome.ToString(),
            response.ErrorCode,
            response.UserSafeMessage,
            response.ObservedState?.ToString(),
            response.Configuration?.ClientId,
            response.Configuration?.GatewayEndpoint,
            response.Configuration?.InterfaceAddress,
            response.Configuration?.AllowedIPs);
    }

    private static PipeResponseEnvelope ErrorResponse(WireGuardOutcome outcome, string errorCode, string message) =>
        new(Success: false, Outcome: outcome.ToString(), ErrorCode: errorCode, UserSafeMessage: message, ObservedState: null);
}
