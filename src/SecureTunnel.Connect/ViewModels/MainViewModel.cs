using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SecureTunnel.Client.Core.Client;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;

namespace SecureTunnel.Connect.ViewModels;

/// <summary>
/// MVVM view model for the shell window. Talks to privileged operations
/// exclusively through <see cref="IPrivilegedClientService"/> (a Named
/// Pipe proxy in production, a fake in tests) - this class never starts a
/// process, touches WireGuard, or reads/writes configuration storage
/// directly, and never holds a raw private key. A successful Connect
/// command only advances <see cref="State"/> toward an intermediate state
/// (<see cref="ClientState.Connecting"/>/<see cref="ClientState.InterfaceActive"/>/
/// <see cref="ClientState.AwaitingHandshake"/>); reaching
/// <see cref="ClientState.Connected"/> requires the service to report
/// that via <see cref="IPrivilegedClientService.GetStatusAsync"/> having
/// corroborated a peer handshake - this view model does not shortcut that,
/// it only ever displays whatever <c>ObservedState</c> the service reports.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IPrivilegedClientService _clientService;
    private string _clientId = string.Empty;
    private ClientState _state = ClientState.NotConfigured;
    private WireGuardOutcome? _lastOutcome;
    private string? _lastMessage;
    private ConfigurationSummary? _currentConfiguration;

    public MainViewModel(IPrivilegedClientService clientService)
    {
        _clientService = clientService;
        Import = new ImportViewModel(clientService);
        Diagnostics = new DiagnosticsViewModel(clientService);
        Import.ImportSucceeded += (_, summary) =>
        {
            ClientId = summary.ClientId;
            CurrentConfiguration = summary;
        };

        ConnectCommand = new RelayCommand(async () => await ConnectAsync());
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync());
        RefreshStatusCommand = new RelayCommand(async () => await RefreshStatusAsync());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImportViewModel Import { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand RefreshStatusCommand { get; }

    public string ClientId
    {
        get => _clientId;
        private set
        {
            _clientId = value;
            OnPropertyChanged();
        }
    }

    public ConfigurationSummary? CurrentConfiguration
    {
        get => _currentConfiguration;
        private set
        {
            _currentConfiguration = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasConfiguration));
            OnPropertyChanged(nameof(AllowedIPsText));
        }
    }

    public bool HasConfiguration => _currentConfiguration is not null;

    public string AllowedIPsText => _currentConfiguration is null ? string.Empty : string.Join(", ", _currentConfiguration.AllowedIPs);

    public ClientState State
    {
        get => _state;
        private set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    /// <summary>
    /// A short, user-facing label distinguishing the situations the spec
    /// calls out explicitly - never a raw outcome/exception string.
    /// </summary>
    public string StatusText => _lastOutcome switch
    {
        WireGuardOutcome.ServiceUnavailable => "Service unavailable - the SecureTunnel Agent could not be reached.",
        WireGuardOutcome.PipeConnectionFailed => "Connection to the SecureTunnel Agent was lost.",
        WireGuardOutcome.AccessDenied => "Access denied by the SecureTunnel Agent.",
        WireGuardOutcome.WireGuardNotInstalled => "WireGuard is not installed on this computer.",
        WireGuardOutcome.PermissionDenied => "Administrator permission is required.",
        WireGuardOutcome.InvalidConfiguration => "The selected configuration is invalid.",
        WireGuardOutcome.ConnectionFailed => _lastMessage is null ? "Connection failed." : $"Connection failed - {_lastMessage}",
        WireGuardOutcome.DisconnectFailed => _lastMessage is null ? "Disconnect failed." : $"Disconnect failed - {_lastMessage}",
        _ => BuildStateStatusText()
    };

    public async Task ConnectAsync()
    {
        var result = await _clientService.ConnectAsync(_clientId, CancellationToken.None);
        Apply(result);
    }

    public async Task DisconnectAsync()
    {
        var result = await _clientService.DisconnectAsync(_clientId, CancellationToken.None);
        Apply(result);
    }

    public async Task RefreshStatusAsync()
    {
        var result = await _clientService.GetStatusAsync(_clientId, CancellationToken.None);
        Apply(result);
        await Diagnostics.RefreshAsync(_clientId, CancellationToken.None);
    }

    private void Apply(PrivilegedClientResult result)
    {
        _lastOutcome = result.Success ? null : result.Outcome;
        _lastMessage = result.UserSafeMessage;

        if (result.ObservedState is { } observed)
        {
            State = observed;
        }

        OnPropertyChanged(nameof(StatusText));
    }

    private string BuildStateStatusText() =>
        _lastMessage is null ? $"Status: {_state}" : $"Status: {_state} - {_lastMessage}";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
