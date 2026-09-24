using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.TestSupport;

namespace SecureTunnel.Client.Agent.Tests.Service;

/// <summary>
/// TEST DOUBLE VERIFICATION - LOCAL ACCEPTANCE ONLY. Proves each named
/// <see cref="FakeWireGuardClientService.FakeScenario"/> configures the
/// double the way its name promises, so other tests that rely on a
/// scenario name can trust it. This is standalone-client verification of
/// the test double itself, not of real WireGuard behavior.
/// </summary>
public class FakeWireGuardClientServiceScenarioTests
{
    private static ClientConfiguration SampleConfiguration() => new()
    {
        ClientId = "client-1",
        GatewayName = "Gateway",
        GatewayEndpoint = "gateway.example.com:51820",
        InterfaceAddress = "10.0.0.2/32",
        PeerPublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=",
        PeerEndpoint = "gateway.example.com:51820",
        AllowedIPs = ["10.10.0.0/24"]
    };

    [Fact]
    public async Task WireGuardNotInstalled_ConnectAndStatusReportWireGuardNotInstalled()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.WireGuardNotInstalled };

        var connect = await fake.ConnectAsync(SampleConfiguration(), CancellationToken.None);
        var status = await fake.GetStatusAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.WireGuardNotInstalled, connect.Outcome);
        Assert.Equal(WireGuardOutcome.WireGuardNotInstalled, status.Outcome);
    }

    [Fact]
    public async Task InvalidConfiguration_ConnectReportsInvalidConfiguration()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.InvalidConfiguration };

        var result = await fake.ConnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.InvalidConfiguration, result.Outcome);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task PermissionDenied_ConnectAndDisconnectReportPermissionDenied()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.PermissionDenied };

        var connect = await fake.ConnectAsync(SampleConfiguration(), CancellationToken.None);
        var disconnect = await fake.DisconnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.PermissionDenied, connect.Outcome);
        Assert.Equal(WireGuardOutcome.PermissionDenied, disconnect.Outcome);
    }

    [Fact]
    public async Task ProcessStartFailure_ConnectReportsProcessStartFailed()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.ProcessStartFailure };

        var result = await fake.ConnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.ProcessStartFailed, result.Outcome);
    }

    [Fact]
    public async Task ProcessTimeout_ConnectReportsConnectionFailed()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.ProcessTimeout };

        var result = await fake.ConnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.ConnectionFailed, result.Outcome);
        Assert.False(fake.KillRequested);
    }

    [Fact]
    public async Task ProcessTimeoutThenKilled_RecordsKillRequested()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.ProcessTimeoutThenKilled };

        var result = await fake.ConnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.ConnectionFailed, result.Outcome);
        Assert.True(fake.KillRequested);
    }

    [Fact]
    public async Task InterfaceCreationSuccess_StatusReportsInterfaceActive_NeverConnected()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.InterfaceCreationSuccess };

        var status = await fake.GetStatusAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(ClientState.InterfaceActive, status.ObservedState);
        Assert.NotEqual(ClientState.Connected, status.ObservedState);
    }

    [Fact]
    public async Task InterfaceActiveAwaitingHandshake_StatusReportsAwaitingHandshake_NeverConnected()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.InterfaceActiveAwaitingHandshake };

        var status = await fake.GetStatusAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(ClientState.AwaitingHandshake, status.ObservedState);
        Assert.NotEqual(ClientState.Connected, status.ObservedState);
    }

    [Fact]
    public async Task HandshakeSuccess_IsTheOnlyScenarioReportingConnected()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.HandshakeSuccess };

        var status = await fake.GetStatusAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(ClientState.Connected, status.ObservedState);
        Assert.Equal(WireGuardOutcome.Connected, status.Outcome);
    }

    [Fact]
    public async Task StatusUnavailable_IsDistinguishedFromDisconnected()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.StatusUnavailable };

        var status = await fake.GetStatusAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.StatusUnavailable, status.Outcome);
        Assert.NotEqual(WireGuardOutcome.Disconnected, status.Outcome);
        Assert.False(status.Success);
    }

    [Fact]
    public async Task DisconnectSuccess_ReportsDisconnected()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.DisconnectSuccess };

        var result = await fake.DisconnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(WireGuardOutcome.Disconnected, result.Outcome);
    }

    [Fact]
    public async Task DisconnectFailure_ReportsDisconnectFailed()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.DisconnectFailure };

        var result = await fake.DisconnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(WireGuardOutcome.DisconnectFailed, result.Outcome);
    }

    [Fact]
    public async Task RepeatedDisconnect_RemainsSafe_EachCallSucceeds()
    {
        var fake = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.RepeatedDisconnectSafe };

        var first = await fake.DisconnectAsync(SampleConfiguration(), CancellationToken.None);
        var second = await fake.DisconnectAsync(SampleConfiguration(), CancellationToken.None);
        var third = await fake.DisconnectAsync(SampleConfiguration(), CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(third.Success);
        Assert.Equal(3, fake.DisconnectCallCount);
    }

    [Fact]
    public async Task UnrelatedConfiguration_IsNeverAffectedByAnotherClientsScenario()
    {
        // Two independent fakes (as two independent ClientConfigurations
        // would be, in the real WireGuardClientService, which scopes every
        // operation to the caller's own ClientId - see
        // docs/wireguard-connection-lifecycle.md) never share state.
        var connectedClient = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.HandshakeSuccess };
        var unrelatedClient = new FakeWireGuardClientService { Scenario = FakeWireGuardClientService.FakeScenario.Disconnected };

        var connectedStatus = await connectedClient.GetStatusAsync(SampleConfiguration(), CancellationToken.None);
        var unrelatedStatus = await unrelatedClient.GetStatusAsync(SampleConfiguration(), CancellationToken.None);

        Assert.Equal(ClientState.Connected, connectedStatus.ObservedState);
        Assert.Equal(ClientState.Disconnected, unrelatedStatus.ObservedState);
    }
}
