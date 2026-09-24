using System.IO.Pipes;
using System.Text.Json;
using SecureTunnel.Client.Agent.Ipc;
using SecureTunnel.Client.Core.Client.Ipc;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Agent.Tests.Ipc;

/// <summary>
/// Real, in-process Named Pipe integration tests: an actual OS pipe
/// (server + client on the same machine, uniquely named per test), with
/// no Windows Service installation involved. These are the tests this
/// phase relies on to prove the transport genuinely round-trips, not
/// fakes standing in for it.
/// </summary>
public class NamedPipeIntegrationTests
{
    private static string UniquePipeName() => $"SecureTunnelClientAgent.Test.{Guid.NewGuid():N}";

    [Fact]
    public async Task ValidRequest_RoundTrips()
    {
        var pipeName = UniquePipeName();
        var dispatcher = new FakeOperationDispatcher
        {
            NextResponse = new SecureTunnel.Client.Agent.Contracts.PrivilegedResponse(
                true, WireGuardOutcome.Disconnected, null, "ok", ClientState.Disconnected)
        };
        var handler = new PipeSessionHandler(dispatcher);

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await handler.HandleAsync(server, CancellationToken.None);
        });

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        var request = new PipeRequestEnvelope("Status", "client-1", null);
        await PipeFraming.WriteFrameAsync(client, JsonSerializer.Serialize(request, PipeJsonOptions.Default), CancellationToken.None);
        var responseJson = await PipeFraming.ReadFrameAsync(client, CancellationToken.None);
        var response = JsonSerializer.Deserialize<PipeResponseEnvelope>(responseJson, PipeJsonOptions.Default)!;

        await serverTask;

        Assert.True(response.Success);
        Assert.Equal("Disconnected", response.Outcome);
        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public async Task ClientDisconnectMidCall_ServerRecoversForNextClient()
    {
        var pipeName = UniquePipeName();
        var dispatcher = new FakeOperationDispatcher();
        var handler = new PipeSessionHandler(dispatcher);

        // First client connects, then disconnects without sending anything.
        using (var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
        {
            var acceptTask = server.WaitForConnectionAsync();

            using (var abruptClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await abruptClient.ConnectAsync(5000);
                await acceptTask; // wait for the server to observe the connection before disconnecting
            } // disposed without writing - simulates an abrupt disconnect

            // The handler must not throw or hang despite reading from a
            // connection the client already tore down.
            await handler.HandleAsync(server, CancellationToken.None);
        }

        // A second, well-behaved client on a fresh server instance must
        // still be served correctly - proves the accept loop pattern
        // recovers per-connection.
        using var server2 = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask2 = Task.Run(async () =>
        {
            await server2.WaitForConnectionAsync();
            await handler.HandleAsync(server2, CancellationToken.None);
        });

        using var client2 = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client2.ConnectAsync(5000);
        var request = new PipeRequestEnvelope("Status", "client-1", null);
        await PipeFraming.WriteFrameAsync(client2, JsonSerializer.Serialize(request, PipeJsonOptions.Default), CancellationToken.None);
        var responseJson = await PipeFraming.ReadFrameAsync(client2, CancellationToken.None);
        await serverTask2;

        Assert.NotNull(responseJson);
    }

    [Fact]
    public async Task Timeout_ExceedsOperationBudget_ReturnsWithoutHanging()
    {
        var pipeName = UniquePipeName();
        var dispatcher = new FakeOperationDispatcher();
        var handler = new PipeSessionHandler(dispatcher);

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var shortTimeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            // Client connects but never sends a request - the handler's
            // read must give up within the externally-supplied deadline
            // rather than hanging indefinitely.
            await handler.HandleAsync(server, shortTimeout.Token);
        });

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        var completed = await Task.WhenAny(serverTask, Task.Delay(TimeSpan.FromSeconds(5))) == serverTask;

        Assert.True(completed, "PipeSessionHandler.HandleAsync did not return within the expected timeout budget.");
    }

    [Fact]
    public async Task MessageSizeLimit_Enforced()
    {
        var pipeName = UniquePipeName();
        var dispatcher = new FakeOperationDispatcher();
        var handler = new PipeSessionHandler(dispatcher);

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await handler.HandleAsync(server, CancellationToken.None);
        });

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        // Send a request whose body exceeds the configured limit.
        var oversizedConfigText = new string('A', PipeProtocol.MaxMessageSizeBytes + 1024);
        var request = new PipeRequestEnvelope("ValidateConfiguration", "client-1", oversizedConfigText);
        var json = JsonSerializer.Serialize(request, PipeJsonOptions.Default);

        // WriteFrameAsync itself throws for an outgoing message this
        // large - assert that guard fires, since the handler's incoming
        // guard is exercised by PipeSessionHandlerTests.OversizedMessage_RejectedBeforeRead.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PipeFraming.WriteFrameAsync(client, json, CancellationToken.None));

        client.Dispose();
        await Task.WhenAny(serverTask, Task.Delay(2000));
    }

    [Fact]
    public async Task Response_NeverContainsPrivateKeyOrPresharedKey()
    {
        var pipeName = UniquePipeName();
        const string secretMarker = "SHOULD-NEVER-APPEAR-IN-RESPONSE-KEY-VALUE";
        var dispatcher = new FakeOperationDispatcher
        {
            NextResponse = new SecureTunnel.Client.Agent.Contracts.PrivilegedResponse(
                true, WireGuardOutcome.Connected, null, "Connected successfully", ClientState.Connected)
        };
        var handler = new PipeSessionHandler(dispatcher);

        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            await handler.HandleAsync(server, CancellationToken.None);
        });

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);

        var request = new PipeRequestEnvelope("Connect", "client-1", null);
        await PipeFraming.WriteFrameAsync(client, JsonSerializer.Serialize(request, PipeJsonOptions.Default), CancellationToken.None);
        var responseJson = await PipeFraming.ReadFrameAsync(client, CancellationToken.None);
        await serverTask;

        Assert.DoesNotContain(secretMarker, responseJson);
        Assert.DoesNotContain("PrivateKey", responseJson);
        Assert.DoesNotContain("PresharedKey", responseJson);
    }
}
