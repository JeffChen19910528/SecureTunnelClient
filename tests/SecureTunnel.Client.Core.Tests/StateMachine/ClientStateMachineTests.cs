using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.StateMachine;

namespace SecureTunnel.Client.Core.Tests.StateMachine;

public class ClientStateMachineTests
{
    public static IEnumerable<object[]> ValidTransitions =>
        new List<object[]>
        {
            new object[] { ClientState.NotConfigured, ClientStateTrigger.ImportConfiguration, ClientState.ConfigurationImported },
            new object[] { ClientState.ConfigurationImported, ClientStateTrigger.ValidationStarted, ClientState.Validating },
            new object[] { ClientState.Validating, ClientStateTrigger.ValidationSucceeded, ClientState.Disconnected },
            new object[] { ClientState.Validating, ClientStateTrigger.ValidationFailed, ClientState.Error },
            new object[] { ClientState.Disconnected, ClientStateTrigger.ConnectCommandIssued, ClientState.Connecting },
            new object[] { ClientState.Connecting, ClientStateTrigger.InterfaceCameUp, ClientState.InterfaceActive },
            new object[] { ClientState.InterfaceActive, ClientStateTrigger.HandshakeObserved, ClientState.AwaitingHandshake },
            new object[] { ClientState.AwaitingHandshake, ClientStateTrigger.ConnectionConfirmed, ClientState.Connected },
            new object[] { ClientState.Connecting, ClientStateTrigger.ConnectionFailed, ClientState.Error },
            new object[] { ClientState.InterfaceActive, ClientStateTrigger.ConnectionFailed, ClientState.Error },
            new object[] { ClientState.AwaitingHandshake, ClientStateTrigger.ConnectionFailed, ClientState.Error },
            new object[] { ClientState.Connecting, ClientStateTrigger.PolicyBlocked, ClientState.Blocked },
            new object[] { ClientState.Connected, ClientStateTrigger.DisconnectCommandIssued, ClientState.Disconnecting },
            new object[] { ClientState.Disconnecting, ClientStateTrigger.DisconnectionConfirmed, ClientState.Disconnected },
            new object[] { ClientState.Disconnecting, ClientStateTrigger.ConnectionFailed, ClientState.Error },
            new object[] { ClientState.Disconnected, ClientStateTrigger.PolicyBlocked, ClientState.Blocked },
            new object[] { ClientState.Connected, ClientStateTrigger.PolicyBlocked, ClientState.Blocked },
            new object[] { ClientState.Disconnecting, ClientStateTrigger.PolicyBlocked, ClientState.Blocked },
            new object[] { ClientState.Error, ClientStateTrigger.ErrorCleared, ClientState.Disconnected },
            new object[] { ClientState.Blocked, ClientStateTrigger.BlockCleared, ClientState.Disconnected },
            new object[] { ClientState.Disconnected, ClientStateTrigger.AgentUnavailable, ClientState.ServiceUnavailable },
            new object[] { ClientState.Connecting, ClientStateTrigger.AgentUnavailable, ClientState.ServiceUnavailable },
            new object[] { ClientState.InterfaceActive, ClientStateTrigger.AgentUnavailable, ClientState.ServiceUnavailable },
            new object[] { ClientState.AwaitingHandshake, ClientStateTrigger.AgentUnavailable, ClientState.ServiceUnavailable },
            new object[] { ClientState.Connected, ClientStateTrigger.AgentUnavailable, ClientState.ServiceUnavailable },
            new object[] { ClientState.Disconnecting, ClientStateTrigger.AgentUnavailable, ClientState.ServiceUnavailable },
            new object[] { ClientState.ServiceUnavailable, ClientStateTrigger.AgentRecovered, ClientState.Disconnected },
        };

    public static IEnumerable<object[]> InvalidTransitions =>
        new List<object[]>
        {
            new object[] { ClientState.NotConfigured, ClientStateTrigger.ConnectCommandIssued },
            new object[] { ClientState.Disconnected, ClientStateTrigger.ConnectionConfirmed },
            new object[] { ClientState.Connecting, ClientStateTrigger.DisconnectCommandIssued },
            new object[] { ClientState.Connected, ClientStateTrigger.ConnectionConfirmed },
            new object[] { ClientState.Error, ClientStateTrigger.ConnectCommandIssued },
            new object[] { ClientState.Blocked, ClientStateTrigger.ConnectCommandIssued },
            // Connected must never be reached by skipping the intermediate
            // verification states - ConnectionConfirmed is illegal from
            // every state except AwaitingHandshake.
            new object[] { ClientState.Connecting, ClientStateTrigger.ConnectionConfirmed },
            new object[] { ClientState.InterfaceActive, ClientStateTrigger.ConnectionConfirmed },
            // Repeated Connect while already connecting/connected must not
            // create a conflicting second operation - there is no legal
            // ConnectCommandIssued trigger from any of these states.
            new object[] { ClientState.Connecting, ClientStateTrigger.ConnectCommandIssued },
            new object[] { ClientState.InterfaceActive, ClientStateTrigger.ConnectCommandIssued },
            new object[] { ClientState.AwaitingHandshake, ClientStateTrigger.ConnectCommandIssued },
            new object[] { ClientState.Connected, ClientStateTrigger.ConnectCommandIssued },
            new object[] { ClientState.Disconnecting, ClientStateTrigger.ConnectCommandIssued },
            // Repeated Disconnect while not connected must not create a
            // conflicting second operation.
            new object[] { ClientState.Disconnected, ClientStateTrigger.DisconnectCommandIssued },
            new object[] { ClientState.Disconnecting, ClientStateTrigger.DisconnectCommandIssued },
            // ServiceUnavailable must only be reached via AgentUnavailable,
            // never conflated with a WireGuard-level failure trigger.
            new object[] { ClientState.ServiceUnavailable, ClientStateTrigger.ConnectionFailed },
            new object[] { ClientState.Error, ClientStateTrigger.AgentUnavailable },
        };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void Fire_ValidTransition_UpdatesState(ClientState from, ClientStateTrigger trigger, ClientState expected)
    {
        var machine = new ClientStateMachine(from);

        var result = machine.Fire(trigger);

        Assert.Equal(expected, result);
        Assert.Equal(expected, machine.Current);
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void Fire_InvalidTransition_ThrowsInvalidStateTransitionException(ClientState from, ClientStateTrigger trigger)
    {
        var machine = new ClientStateMachine(from);

        var exception = Assert.Throws<InvalidStateTransitionException>(() => machine.Fire(trigger));

        Assert.Equal(from, exception.FromState);
        Assert.Equal(trigger, exception.AttemptedTrigger);
        Assert.Equal(from, machine.Current);
    }

    [Fact]
    public void ConnectCommandIssued_NeverDirectlyReachesConnected()
    {
        var machine = new ClientStateMachine(ClientState.Disconnected);

        var afterConnectCommand = machine.Fire(ClientStateTrigger.ConnectCommandIssued);

        Assert.Equal(ClientState.Connecting, afterConnectCommand);
        Assert.NotEqual(ClientState.Connected, afterConnectCommand);
    }

    [Fact]
    public void ConnectionConfirmed_OnlyLegalFrom_AwaitingHandshake()
    {
        foreach (var state in Enum.GetValues<ClientState>())
        {
            var machine = new ClientStateMachine(state);
            var canConfirm = machine.CanTransition(ClientStateTrigger.ConnectionConfirmed);

            Assert.Equal(state == ClientState.AwaitingHandshake, canConfirm);
        }
    }

    [Fact]
    public void FullHappyPath_Disconnected_To_Connected_RequiresAllIntermediateStates()
    {
        var machine = new ClientStateMachine(ClientState.Disconnected);

        Assert.Equal(ClientState.Connecting, machine.Fire(ClientStateTrigger.ConnectCommandIssued));
        Assert.Equal(ClientState.InterfaceActive, machine.Fire(ClientStateTrigger.InterfaceCameUp));
        Assert.Equal(ClientState.AwaitingHandshake, machine.Fire(ClientStateTrigger.HandshakeObserved));
        Assert.Equal(ClientState.Connected, machine.Fire(ClientStateTrigger.ConnectionConfirmed));
    }
}
