using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using SecureTunnel.Client.Core.Client;
using SecureTunnel.Client.Core.Diagnostics;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Connect.ViewModels;

/// <summary>
/// Wraps a <see cref="DiagnosticSnapshot"/> for display. Every value
/// exposed here is a boolean, enum, or the already-sanitized fields of a
/// <see cref="DiagnosticResult"/> - never a private key, preshared key, or
/// raw process output. <see cref="BuildClipboardText"/> builds its output
/// only from these typed fields, never from anything unsanitized.
/// </summary>
public sealed class DiagnosticsViewModel : INotifyPropertyChanged
{
    private readonly IPrivilegedClientService _clientService;
    private DiagnosticSnapshot _snapshot = new(false, false, false, null, null, null);

    public DiagnosticsViewModel(IPrivilegedClientService clientService)
    {
        _clientService = clientService;
        CopyToClipboardCommand = new RelayCommand(() =>
        {
            System.Windows.Clipboard.SetText(BuildClipboardText());
            return Task.CompletedTask;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand CopyToClipboardCommand { get; }

    public bool AgentAvailable => _snapshot.AgentAvailable;

    public bool PipeAvailable => _snapshot.PipeAvailable;

    public bool WireGuardExecutableAvailable => _snapshot.WireGuardExecutableAvailable;

    public async Task RefreshAsync(string clientId, CancellationToken cancellationToken)
    {
        var result = await _clientService.GetStatusAsync(clientId, cancellationToken);

        var agentReachable = result.Outcome is not (WireGuardOutcome.ServiceUnavailable or WireGuardOutcome.PipeConnectionFailed or WireGuardOutcome.AccessDenied);
        var wireGuardInstalled = result.Outcome != WireGuardOutcome.WireGuardNotInstalled;

        var lastOperation = new DiagnosticResult(
            Operation: "GetStatus",
            Result: result.Success ? DiagnosticOutcome.Success : DiagnosticOutcome.Failure,
            ErrorCode: result.ErrorCode ?? string.Empty,
            UserSafeMessage: DiagnosticResultFactory.Sanitize(result.UserSafeMessage ?? string.Empty),
            TechnicalDetails: DiagnosticResultFactory.Sanitize($"Outcome={result.Outcome}"),
            TimestampUtc: DateTime.UtcNow,
            IsRetryable: result.Outcome is WireGuardOutcome.ServiceUnavailable or WireGuardOutcome.PipeConnectionFailed);

        _snapshot = new DiagnosticSnapshot(
            AgentAvailable: agentReachable,
            PipeAvailable: agentReachable,
            WireGuardExecutableAvailable: wireGuardInstalled,
            LastValidation: null,
            InterfaceState: result.ObservedState,
            LastOperation: lastOperation);

        OnPropertyChanged(nameof(AgentAvailable));
        OnPropertyChanged(nameof(PipeAvailable));
        OnPropertyChanged(nameof(WireGuardExecutableAvailable));
        OnPropertyChanged(nameof(LastOperationSummary));
    }

    public string LastOperationSummary => _snapshot.LastOperation is { } op
        ? $"{op.Operation}: {op.Result} ({op.ErrorCode}) at {op.TimestampUtc:u}"
        : "No operation performed yet.";

    public string BuildClipboardText()
    {
        var op = _snapshot.LastOperation;
        return string.Join(Environment.NewLine,
        [
            $"Agent available: {_snapshot.AgentAvailable}",
            $"Pipe available: {_snapshot.PipeAvailable}",
            $"WireGuard executable available: {_snapshot.WireGuardExecutableAvailable}",
            $"Interface state: {_snapshot.InterfaceState}",
            $"Last operation: {op?.Operation ?? "(none)"}",
            $"Last result: {op?.Result}",
            $"Last error code: {op?.ErrorCode}",
            $"Last message: {op?.UserSafeMessage}",
            $"Last timestamp (UTC): {op?.TimestampUtc:u}",
            $"Retryable: {op?.IsRetryable}"
        ]);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
