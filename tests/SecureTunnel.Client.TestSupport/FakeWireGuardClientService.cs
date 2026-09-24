using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;
using CoreValidation = SecureTunnel.Client.Core.Validation;

namespace SecureTunnel.Client.TestSupport;

/// <summary>
/// TEST DOUBLE - SIMULATED, LOCAL ACCEPTANCE ONLY. Never shells out to a
/// real WireGuard binary and never reports real network connectivity -
/// every outcome is pre-programmed by the test via <see cref="Scenario"/>
/// (or, for finer control, the individual *Outcome/*ObservedState
/// properties). Use this to exercise <c>WireGuardClientService</c>
/// callers' behavior under each of the standalone-client acceptance
/// scenarios without requiring WireGuard, a Windows Service, or a real
/// Gateway. See docs/c5-standalone-client-acceptance.md.
/// </summary>
public sealed class FakeWireGuardClientService : IWireGuardClientService
{
    /// <summary>
    /// Named, deterministic scenarios covering the standalone acceptance
    /// matrix. Setting <see cref="Scenario"/> configures
    /// <see cref="ConnectOutcome"/>/<see cref="DisconnectOutcome"/>/<see cref="StatusOutcome"/>/<see cref="StatusObservedState"/>
    /// consistently; each is still individually overridable afterward for
    /// bespoke test setups.
    /// </summary>
    public enum FakeScenario
    {
        /// <summary>wireguard.exe is not on PATH.</summary>
        WireGuardNotInstalled,

        /// <summary>The stored/supplied configuration fails validation.</summary>
        InvalidConfiguration,

        /// <summary>The simulated process exits with WireGuard's permission-denied exit code (5).</summary>
        PermissionDenied,

        /// <summary>The simulated process could not be started at all.</summary>
        ProcessStartFailure,

        /// <summary>The simulated operation exceeds its timeout budget.</summary>
        ProcessTimeout,

        /// <summary>Same as <see cref="ProcessTimeout"/>, but the double also records that a kill was requested (mirrors the real runner's kill-on-cancel behavior).</summary>
        ProcessTimeoutThenKilled,

        /// <summary>Interface reported up (no peer entry observed yet).</summary>
        InterfaceCreationSuccess,

        /// <summary>Interface up, peer present, handshake not yet observed.</summary>
        InterfaceActiveAwaitingHandshake,

        /// <summary>Interface up, peer present, handshake observed - the only scenario that may report Connected.</summary>
        HandshakeSuccess,

        /// <summary>A status query itself could not be run or parsed.</summary>
        StatusUnavailable,

        /// <summary>Tunnel successfully torn down.</summary>
        DisconnectSuccess,

        /// <summary>A disconnect attempt reported failure.</summary>
        DisconnectFailure,

        /// <summary>Tunnel already absent - a repeated/idempotent disconnect.</summary>
        RepeatedDisconnectSafe,

        /// <summary>Baseline: no tunnel present, nothing has been attempted.</summary>
        Disconnected
    }

    public FakeScenario Scenario
    {
        set => ApplyScenario(value);
    }

    public WireGuardOutcome ConnectOutcome { get; set; } = WireGuardOutcome.Unknown;
    public WireGuardOutcome DisconnectOutcome { get; set; } = WireGuardOutcome.Disconnected;
    public WireGuardOutcome StatusOutcome { get; set; } = WireGuardOutcome.Disconnected;
    public ClientState StatusObservedState { get; set; } = ClientState.Disconnected;

    /// <summary>
    /// Set true by <see cref="FakeScenario.ProcessTimeoutThenKilled"/> - a
    /// test-only signal that the double "would have" requested a kill,
    /// mirroring (but not replacing) <c>WireGuardProcessRunnerTests</c>'s
    /// real-process kill verification.
    /// </summary>
    public bool KillRequested { get; private set; }

    /// <summary>Number of times DisconnectAsync has been invoked - used to assert repeated-disconnect safety.</summary>
    public int DisconnectCallCount { get; private set; }

    public Task<ImportResult> ImportConfiguration(string rawConfigText, CancellationToken cancellationToken) =>
        Task.FromResult(new ImportResult { Outcome = WireGuardOutcome.Unknown, Success = true });

    public Task<CoreValidation.ValidationResult> ValidateConfiguration(ClientConfiguration configuration, CancellationToken cancellationToken) =>
        Task.FromResult(new CoreValidation.ValidationResult());

    public Task<ConnectResult> ConnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken) =>
        Task.FromResult(new ConnectResult
        {
            Outcome = ConnectOutcome,
            Success = ConnectOutcome is WireGuardOutcome.Unknown or WireGuardOutcome.Connected or WireGuardOutcome.InterfaceActive or WireGuardOutcome.AwaitingHandshake
        });

    public Task<DisconnectResult> DisconnectAsync(ClientConfiguration configuration, CancellationToken cancellationToken)
    {
        DisconnectCallCount++;
        return Task.FromResult(new DisconnectResult
        {
            Outcome = DisconnectOutcome,
            Success = DisconnectOutcome == WireGuardOutcome.Disconnected
        });
    }

    public Task<StatusResult> GetStatusAsync(ClientConfiguration configuration, CancellationToken cancellationToken) =>
        Task.FromResult(new StatusResult { Outcome = StatusOutcome, Success = StatusOutcome != WireGuardOutcome.StatusUnavailable, ObservedState = StatusObservedState });

    private void ApplyScenario(FakeScenario scenario)
    {
        switch (scenario)
        {
            case FakeScenario.WireGuardNotInstalled:
                ConnectOutcome = DisconnectOutcome = StatusOutcome = WireGuardOutcome.WireGuardNotInstalled;
                StatusObservedState = ClientState.Error;
                break;
            case FakeScenario.InvalidConfiguration:
                ConnectOutcome = WireGuardOutcome.InvalidConfiguration;
                break;
            case FakeScenario.PermissionDenied:
                ConnectOutcome = DisconnectOutcome = WireGuardOutcome.PermissionDenied;
                break;
            case FakeScenario.ProcessStartFailure:
                ConnectOutcome = WireGuardOutcome.ProcessStartFailed;
                break;
            case FakeScenario.ProcessTimeout:
                ConnectOutcome = WireGuardOutcome.ConnectionFailed;
                break;
            case FakeScenario.ProcessTimeoutThenKilled:
                ConnectOutcome = WireGuardOutcome.ConnectionFailed;
                KillRequested = true;
                break;
            case FakeScenario.InterfaceCreationSuccess:
                ConnectOutcome = StatusOutcome = WireGuardOutcome.InterfaceActive;
                StatusObservedState = ClientState.InterfaceActive;
                break;
            case FakeScenario.InterfaceActiveAwaitingHandshake:
                StatusOutcome = WireGuardOutcome.AwaitingHandshake;
                StatusObservedState = ClientState.AwaitingHandshake;
                break;
            case FakeScenario.HandshakeSuccess:
                StatusOutcome = WireGuardOutcome.Connected;
                StatusObservedState = ClientState.Connected;
                break;
            case FakeScenario.StatusUnavailable:
                StatusOutcome = WireGuardOutcome.StatusUnavailable;
                StatusObservedState = ClientState.Error;
                break;
            case FakeScenario.DisconnectSuccess:
                DisconnectOutcome = WireGuardOutcome.Disconnected;
                break;
            case FakeScenario.DisconnectFailure:
                DisconnectOutcome = WireGuardOutcome.DisconnectFailed;
                break;
            case FakeScenario.RepeatedDisconnectSafe:
                DisconnectOutcome = WireGuardOutcome.Disconnected;
                StatusOutcome = WireGuardOutcome.Disconnected;
                StatusObservedState = ClientState.Disconnected;
                break;
            case FakeScenario.Disconnected:
                DisconnectOutcome = StatusOutcome = WireGuardOutcome.Disconnected;
                StatusObservedState = ClientState.Disconnected;
                break;
        }
    }
}
