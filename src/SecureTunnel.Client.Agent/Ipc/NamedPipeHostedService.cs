using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureTunnel.Client.Core.Client.Ipc;

namespace SecureTunnel.Client.Agent.Ipc;

/// <summary>
/// Long-running background service that accepts Named Pipe client
/// connections on the fixed, product-owned pipe name
/// (<see cref="PipeProtocol.PipeName"/>) and hands each one off to a
/// <see cref="PipeSessionHandler"/>. One connection is served at a time
/// per accept loop iteration; a new server instance is created for the
/// next client after each connection completes, matching the standard
/// .NET Named Pipe server pattern. A failure handling one client (bad
/// data, disconnect, timeout) never brings down the accept loop for
/// subsequent clients.
/// </summary>
public sealed class NamedPipeHostedService : BackgroundService
{
    private readonly PipeSessionHandler _sessionHandler;
    private readonly ILogger<NamedPipeHostedService> _logger;

    public NamedPipeHostedService(PipeSessionHandler sessionHandler, ILogger<NamedPipeHostedService> logger)
    {
        _sessionHandler = sessionHandler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var server = NamedPipeServerStreamAcl.Create(
                    PipeProtocol.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    PipeSecurityFactory.Create());

                await server.WaitForConnectionAsync(stoppingToken);

                await _sessionHandler.HandleAsync(server, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (IOException ex)
            {
                // A client disconnected abruptly or the pipe was reset;
                // log and continue accepting the next client rather than
                // crashing the service.
                _logger.LogWarning(ex, "Named Pipe session ended with an I/O error.");
            }
        }
    }
}
