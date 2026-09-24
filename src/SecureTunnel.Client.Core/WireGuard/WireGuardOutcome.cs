namespace SecureTunnel.Client.Core.WireGuard;

/// <summary>
/// Distinguishes the possible outcomes of a WireGuard client operation,
/// including outcomes that originate from the Named Pipe transport and
/// privileged service boundary (added in Phase C2). A process exit code
/// alone must never be mapped directly to <see cref="Connected"/> -
/// connectivity must be corroborated via
/// <see cref="IWireGuardClientService.GetStatusAsync"/>. Likewise, a
/// successful pipe round-trip must never be mapped directly to
/// <see cref="Connected"/> either - see
/// <see cref="SecureTunnel.Client.Core.Client.IPrivilegedClientService"/>.
/// </summary>
public enum WireGuardOutcome
{
    WireGuardNotInstalled,
    PermissionDenied,
    InvalidConfiguration,
    ConnectionFailed,
    Connected,
    Disconnected,
    Unknown,

    /// <summary>The privileged service could not be reached at all (e.g. the pipe does not exist / service is not running).</summary>
    ServiceUnavailable,

    /// <summary>A pipe connection was established but failed mid-call (broken pipe, I/O error, timeout).</summary>
    PipeConnectionFailed,

    /// <summary>The pipe connection was rejected by the server's access control.</summary>
    AccessDenied,

    /// <summary>The request was malformed, oversized, or specified an operation outside the allow-list.</summary>
    InvalidRequest,

    /// <summary>The external process could not be started at all (distinct from a process that started and exited unsuccessfully).</summary>
    ProcessStartFailed,

    /// <summary>A disconnect (uninstall) attempt ran but reported failure.</summary>
    DisconnectFailed,

    /// <summary>The status query itself could not be run or its output could not be parsed - distinct from a confirmed-absent tunnel (<see cref="Disconnected"/>).</summary>
    StatusUnavailable,

    /// <summary>The tunnel interface is up but no peer handshake has been observed yet.</summary>
    InterfaceActive,

    /// <summary>A peer entry is present but no successful handshake has occurred yet - not yet <see cref="Connected"/>.</summary>
    AwaitingHandshake
}
