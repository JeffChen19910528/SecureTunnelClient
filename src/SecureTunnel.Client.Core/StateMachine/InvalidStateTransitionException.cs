using SecureTunnel.Client.Core.Models;

namespace SecureTunnel.Client.Core.StateMachine;

/// <summary>
/// Thrown when a trigger is fired that is not permitted from the current
/// state. The message intentionally contains only state/trigger names -
/// never configuration or secret content.
/// </summary>
public sealed class InvalidStateTransitionException : Exception
{
    public ClientState FromState { get; }
    public ClientStateTrigger AttemptedTrigger { get; }

    public InvalidStateTransitionException(ClientState fromState, ClientStateTrigger attemptedTrigger)
        : base($"Trigger '{attemptedTrigger}' is not valid from state '{fromState}'.")
    {
        FromState = fromState;
        AttemptedTrigger = attemptedTrigger;
    }
}
