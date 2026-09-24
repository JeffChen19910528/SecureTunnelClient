using SecureTunnel.Client.Core.Models;

namespace SecureTunnel.Client.Core.StateMachine;

/// <summary>
/// Enforces the client lifecycle transition table. <see cref="ClientState.Connected"/>
/// is reachable only from <see cref="ClientState.AwaitingHandshake"/> via
/// <see cref="ClientStateTrigger.ConnectionConfirmed"/> - there is no
/// shortcut from <see cref="ClientState.Connecting"/> straight to
/// <see cref="ClientState.Connected"/>, and no state may skip
/// <see cref="ClientState.InterfaceActive"/>/<see cref="ClientState.AwaitingHandshake"/>.
/// This prevents the client from ever claiming connectivity based solely
/// on a command having been sent or a Named Pipe call having succeeded -
/// only a verified interface + observed handshake can reach Connected.
/// </summary>
public sealed class ClientStateMachine : IClientStateMachine
{
    private static readonly IReadOnlyDictionary<ClientState, IReadOnlyDictionary<ClientStateTrigger, ClientState>> Transitions =
        BuildTransitionTable();

    public ClientState Current { get; private set; }

    public ClientStateMachine(ClientState initialState = ClientState.NotConfigured)
    {
        Current = initialState;
    }

    public bool CanTransition(ClientStateTrigger trigger) =>
        Transitions[Current].ContainsKey(trigger);

    public ClientState Fire(ClientStateTrigger trigger)
    {
        if (!Transitions[Current].TryGetValue(trigger, out var next))
        {
            throw new InvalidStateTransitionException(Current, trigger);
        }

        Current = next;
        return Current;
    }

    private static IReadOnlyDictionary<ClientState, IReadOnlyDictionary<ClientStateTrigger, ClientState>> BuildTransitionTable()
    {
        var table = new Dictionary<ClientState, IReadOnlyDictionary<ClientStateTrigger, ClientState>>
        {
            [ClientState.NotConfigured] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.ImportConfiguration] = ClientState.ConfigurationImported
            },
            [ClientState.ConfigurationImported] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.ValidationStarted] = ClientState.Validating
            },
            [ClientState.Validating] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.ValidationSucceeded] = ClientState.Disconnected,
                [ClientStateTrigger.ValidationFailed] = ClientState.Error
            },
            [ClientState.Disconnected] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.ConnectCommandIssued] = ClientState.Connecting,
                [ClientStateTrigger.PolicyBlocked] = ClientState.Blocked,
                [ClientStateTrigger.AgentUnavailable] = ClientState.ServiceUnavailable
            },
            [ClientState.Connecting] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.InterfaceCameUp] = ClientState.InterfaceActive,
                [ClientStateTrigger.ConnectionFailed] = ClientState.Error,
                [ClientStateTrigger.PolicyBlocked] = ClientState.Blocked,
                [ClientStateTrigger.AgentUnavailable] = ClientState.ServiceUnavailable
            },
            [ClientState.InterfaceActive] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.HandshakeObserved] = ClientState.AwaitingHandshake,
                [ClientStateTrigger.ConnectionFailed] = ClientState.Error,
                [ClientStateTrigger.AgentUnavailable] = ClientState.ServiceUnavailable
            },
            [ClientState.AwaitingHandshake] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.ConnectionConfirmed] = ClientState.Connected,
                [ClientStateTrigger.ConnectionFailed] = ClientState.Error,
                [ClientStateTrigger.AgentUnavailable] = ClientState.ServiceUnavailable
            },
            [ClientState.Connected] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.DisconnectCommandIssued] = ClientState.Disconnecting,
                [ClientStateTrigger.PolicyBlocked] = ClientState.Blocked,
                [ClientStateTrigger.AgentUnavailable] = ClientState.ServiceUnavailable
            },
            [ClientState.Disconnecting] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.DisconnectionConfirmed] = ClientState.Disconnected,
                [ClientStateTrigger.ConnectionFailed] = ClientState.Error,
                [ClientStateTrigger.PolicyBlocked] = ClientState.Blocked,
                [ClientStateTrigger.AgentUnavailable] = ClientState.ServiceUnavailable
            },
            [ClientState.Error] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.ErrorCleared] = ClientState.Disconnected
            },
            [ClientState.Blocked] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.BlockCleared] = ClientState.Disconnected
            },
            [ClientState.ServiceUnavailable] = new Dictionary<ClientStateTrigger, ClientState>
            {
                [ClientStateTrigger.AgentRecovered] = ClientState.Disconnected
            }
        };

        return table;
    }
}
