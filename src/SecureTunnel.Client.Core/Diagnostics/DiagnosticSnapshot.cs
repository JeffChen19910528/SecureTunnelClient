using SecureTunnel.Client.Core.Models;

namespace SecureTunnel.Client.Core.Diagnostics;

/// <summary>
/// Aggregated, UI-safe diagnostics for display/export. Every field here is
/// either a boolean, an enum, or a <see cref="DiagnosticResult"/> (whose
/// own <c>TechnicalDetails</c> is already sanitized by
/// <see cref="DiagnosticResultFactory"/>) - never a raw process output
/// string or secret.
/// </summary>
public sealed record DiagnosticSnapshot(
    bool AgentAvailable,
    bool PipeAvailable,
    bool WireGuardExecutableAvailable,
    DiagnosticResult? LastValidation,
    ClientState? InterfaceState,
    DiagnosticResult? LastOperation);
