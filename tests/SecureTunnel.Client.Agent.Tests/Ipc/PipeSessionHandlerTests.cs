using System.Text;
using System.Text.Json;
using SecureTunnel.Client.Agent.Ipc;
using SecureTunnel.Client.Core.Client.Ipc;

namespace SecureTunnel.Client.Agent.Tests.Ipc;

public class PipeSessionHandlerTests
{
    private static async Task<byte[]> FrameAsync(string json)
    {
        using var buffer = new MemoryStream();
        await PipeFraming.WriteFrameAsync(buffer, json, CancellationToken.None);
        return buffer.ToArray();
    }

    private static async Task<PipeResponseEnvelope> RunAsync(FakeOperationDispatcher dispatcher, byte[] requestBytes)
    {
        var handler = new PipeSessionHandler(dispatcher);
        var stream = new DuplexTestStream(requestBytes);

        await handler.HandleAsync(stream, CancellationToken.None);

        stream.Outgoing.Position = 0;
        var responseJson = await PipeFraming.ReadFrameAsync(stream.Outgoing, CancellationToken.None);
        return JsonSerializer.Deserialize<PipeResponseEnvelope>(responseJson, PipeJsonOptions.Default)!;
    }

    [Fact]
    public async Task UnknownOperation_ReturnsInvalidRequest()
    {
        var dispatcher = new FakeOperationDispatcher();
        var envelope = new PipeRequestEnvelope("DeleteEverything", "client-1", null);
        var requestBytes = await FrameAsync(JsonSerializer.Serialize(envelope, PipeJsonOptions.Default));

        var response = await RunAsync(dispatcher, requestBytes);

        Assert.False(response.Success);
        Assert.Equal("InvalidRequest", response.ErrorCode);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task MalformedJson_ReturnsInvalidRequest()
    {
        var dispatcher = new FakeOperationDispatcher();
        var requestBytes = await FrameAsync("{ this is not valid json");

        var response = await RunAsync(dispatcher, requestBytes);

        Assert.False(response.Success);
        Assert.Equal("InvalidRequest", response.ErrorCode);
    }

    [Fact]
    public async Task OversizedMessage_RejectedBeforeRead()
    {
        var dispatcher = new FakeOperationDispatcher();

        // Hand-craft a frame whose length prefix exceeds the limit,
        // without actually providing that many bytes - proves the
        // handler rejects based on the declared length alone.
        var lengthPrefix = BitConverter.GetBytes(PipeProtocol.MaxMessageSizeBytes + 1);
        var requestBytes = lengthPrefix; // no body follows

        var response = await RunAsync(dispatcher, requestBytes);

        Assert.False(response.Success);
        Assert.Equal("InvalidRequest", response.ErrorCode);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task UnsupportedOperation_Rejected()
    {
        var dispatcher = new FakeOperationDispatcher();
        var envelope = new PipeRequestEnvelope("ExecuteCommand", "client-1", null);
        var requestBytes = await FrameAsync(JsonSerializer.Serialize(envelope, PipeJsonOptions.Default));

        var response = await RunAsync(dispatcher, requestBytes);

        Assert.False(response.Success);
        Assert.Equal("InvalidRequest", response.ErrorCode);
    }

    [Fact]
    public async Task ValidRequest_DispatchesAndReturnsStructuredResponse()
    {
        var dispatcher = new FakeOperationDispatcher();
        var envelope = new PipeRequestEnvelope("Status", "client-1", null);
        var requestBytes = await FrameAsync(JsonSerializer.Serialize(envelope, PipeJsonOptions.Default));

        var response = await RunAsync(dispatcher, requestBytes);

        Assert.True(response.Success);
        Assert.Single(dispatcher.Requests);
        Assert.IsType<SecureTunnel.Client.Agent.Contracts.StatusRequest>(dispatcher.Requests[0]);
    }

    [Fact]
    public async Task StructuredErrorResponse_NeverThrowsToClient()
    {
        var dispatcher = new FakeOperationDispatcher();
        // Garbage bytes whose first 4 bytes, read as a length prefix,
        // resolve to a value far exceeding the size limit - proves the
        // handler converts this into a structured InvalidRequest response
        // rather than letting an exception escape to the pipe accept loop.
        var requestBytes = Encoding.UTF8.GetBytes("not even a length-prefixed frame at all, too short");

        var response = await RunAsync(dispatcher, requestBytes);

        Assert.False(response.Success);
        Assert.Equal("InvalidRequest", response.ErrorCode);
        Assert.Empty(dispatcher.Requests);
    }
}
