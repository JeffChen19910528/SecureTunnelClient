namespace SecureTunnel.Client.Core.Models;

/// <summary>
/// Events that drive <see cref="ClientState"/> transitions. Note that
/// issuing a connect/disconnect command and confirming its outcome are
/// distinct triggers, so the state machine never claims connectivity
/// purely because a command was sent.
/// </summary>
public enum ClientStateTrigger
{
    ImportConfiguration,

    ValidationStarted,
    ValidationSucceeded,
    ValidationFailed,

    ConnectCommandIssued,

    /// <summary>The tunnel interface has been observed to be up.</summary>
    InterfaceCameUp,

    /// <summary>A peer entry has been observed (handshake not yet confirmed).</summary>
    HandshakeObserved,

    ConnectionConfirmed,
    ConnectionFailed,
    DisconnectCommandIssued,
    DisconnectionConfirmed,
    ErrorCleared,
    PolicyBlocked,
    BlockCleared,

    /// <summary>The privileged Agent could not be reached.</summary>
    AgentUnavailable,

    /// <summary>The privileged Agent has become reachable again.</summary>
    AgentRecovered
}
