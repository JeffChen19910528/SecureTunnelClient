using System.IO.Pipes;
using System.Text.Json;
using SecureTunnel.Client.Core.Client.Ipc;
using SecureTunnel.Client.Infrastructure.Ipc;

namespace SecureTunnel.Client.Infrastructure.Tests.Ipc;

/// <summary>
/// Tests the WPF-facing Named Pipe proxy against a minimal, test-local
/// fake server bound to the same fixed pipe name the proxy uses (the
/// proxy deliberately does not allow the pipe name to be supplied by the
/// caller, so the test server must use the real, fixed name). Runs
/// sequentially within this class (xUnit's default) to avoid colliding
/// pipe-name instances between tests.
/// </summary>
public class NamedPipePrivilegedClientProxyTests
{
    private static async Task RunFakeServerOnceAsync(PipeResponseEnvelope response, TaskCompletionSource ready)
    {
        using var server = new NamedPipeServerStream(PipeProtocol.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        ready.SetResult();
        await server.WaitForConnectionAsync();

        await PipeFraming.ReadFrameAsync(server, CancellationToken.None);
        await PipeFraming.WriteFrameAsync(server, JsonSerializer.Serialize(response, PipeJsonOptions.Default), CancellationToken.None);
    }

    [Fact]
    public async Task Connect_Success()
    {
        var ready = new TaskCompletionSource();
        var serverTask = RunFakeServerOnceAsync(
            new PipeResponseEnvelope(true, "Unknown", null, "Connect command sent", "Connecting"),
            ready);
        await ready.Task;

        var proxy = new NamedPipePrivilegedClientProxy();
        var result = await proxy.ConnectAsync("client-1", CancellationToken.None);
        await serverTask;

        Assert.True(result.Success);
        Assert.Equal("Connecting", result.ObservedState?.ToString());
    }

    [Fact]
    public async Task Disconnect_Success()
    {
        var ready = new TaskCompletionSource();
        var serverTask = RunFakeServerOnceAsync(
            new PipeResponseEnvelope(true, "Disconnected", null, null, "Disconnected"),
            ready);
        await ready.Task;

        var proxy = new NamedPipePrivilegedClientProxy();
        var result = await proxy.DisconnectAsync("client-1", CancellationToken.None);
        await serverTask;

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Status_Success()
    {
        var ready = new TaskCompletionSource();
        var serverTask = RunFakeServerOnceAsync(
            new PipeResponseEnvelope(true, "Connected", null, null, "Connected"),
            ready);
        await ready.Task;

        var proxy = new NamedPipePrivilegedClientProxy();
        var result = await proxy.GetStatusAsync("client-1", CancellationToken.None);
        await serverTask;

        Assert.True(result.Success);
        Assert.Equal(Core.Models.ClientState.Connected, result.ObservedState);
    }

    [Fact]
    public async Task InvalidConfiguration_Propagates()
    {
        var ready = new TaskCompletionSource();
        var serverTask = RunFakeServerOnceAsync(
            new PipeResponseEnvelope(false, "InvalidConfiguration", "InvalidConfiguration", "The configuration is invalid.", null),
            ready);
        await ready.Task;

        var proxy = new NamedPipePrivilegedClientProxy();
        var result = await proxy.ValidateConfigurationAsync("not a real config", CancellationToken.None);
        await serverTask;

        Assert.False(result.Success);
        Assert.Equal("InvalidConfiguration", result.ErrorCode);
    }

    [Fact]
    public async Task ServiceNotRunning_ReturnsServiceUnavailable()
    {
        // No fake server is started at all - this is the "service isn't
        // running" scenario.
        var proxy = new NamedPipePrivilegedClientProxy();

        var result = await proxy.GetStatusAsync("client-1", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(Core.WireGuard.WireGuardOutcome.ServiceUnavailable, result.Outcome);
    }
}
