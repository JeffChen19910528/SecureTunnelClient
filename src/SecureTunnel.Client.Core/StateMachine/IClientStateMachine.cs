using SecureTunnel.Client.Core.Models;

namespace SecureTunnel.Client.Core.StateMachine;

public interface IClientStateMachine
{
    ClientState Current { get; }

    bool CanTransition(ClientStateTrigger trigger);

    /// <summary>
    /// Applies <paramref name="trigger"/> and returns the new state.
    /// Throws <see cref="InvalidStateTransitionException"/> if the trigger
    /// is not allowed from the current state.
    /// </summary>
    ClientState Fire(ClientStateTrigger trigger);
}
