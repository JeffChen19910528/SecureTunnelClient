using SecureTunnel.Client.Agent.Contracts;
using SecureTunnel.Client.Agent.Service;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Client.Agent.Tests.Ipc;

/// <summary>Test double - not a real dispatcher. Returns a pre-programmed response for any request.</summary>
internal sealed class FakeOperationDispatcher : IPrivilegedOperationDispatcher
{
    public PrivilegedResponse NextResponse { get; set; } =
        new(true, WireGuardOutcome.Unknown, null, null, ClientState.Disconnected);

    public List<PrivilegedRequest> Requests { get; } = [];

    public Task<PrivilegedResponse> DispatchAsync(PrivilegedRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(NextResponse);
    }
}
