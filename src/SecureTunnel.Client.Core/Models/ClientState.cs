namespace SecureTunnel.Client.Core.Models;

/// <summary>
/// Explicit lifecycle states for the client. See
/// <see cref="SecureTunnel.Client.Core.StateMachine.ClientStateMachine"/>
/// for the allowed transition table. <see cref="Connected"/> must only be
/// reached via a status-confirmation trigger, never merely because a
/// connect command was issued.
/// </summary>
public enum ClientState
{
    NotConfigured,
    ConfigurationImported,

    /// <summary>Imported configuration is being validated.</summary>
    Validating,

    Disconnected,
    Connecting,

    /// <summary>The tunnel interface is up but no peer handshake has been observed yet.</summary>
    InterfaceActive,

    /// <summary>A peer entry is present but no successful handshake has occurred yet.</summary>
    AwaitingHandshake,

    Connected,
    Disconnecting,
    Error,
    Blocked,

    /// <summary>The privileged Agent could not be reached (Named Pipe/service unavailable).</summary>
    ServiceUnavailable
}
