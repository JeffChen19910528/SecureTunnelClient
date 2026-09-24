using System.Windows;
using SecureTunnel.Client.Infrastructure.Ipc;
using SecureTunnel.Connect.Services;
using SecureTunnel.Connect.ViewModels;

namespace SecureTunnel.Connect;

/// <summary>
/// Application bootstrap. Constructs the Named-Pipe-backed
/// <see cref="NamedPipePrivilegedClientProxy"/> and injects it into
/// <see cref="MainViewModel"/> - this is the only place the UI wires up
/// its (non-privileged) proxy to the privileged boundary. No privileged
/// type from SecureTunnel.Client.Agent or SecureTunnel.Client.Infrastructure's
/// WireGuard/Storage namespaces is referenced here. Also owns the
/// <see cref="TrayIconService"/> lifetime: closing the main window hides
/// it to the tray rather than exiting, and only the tray's explicit
/// "Exit" item (or an OS shutdown) actually terminates the process - in
/// neither case is the Agent service or an active tunnel touched.
/// </summary>
public partial class App : System.Windows.Application
{
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var clientService = new NamedPipePrivilegedClientProxy();
        var viewModel = new MainViewModel(clientService);

        _mainWindow = new MainWindow(viewModel);
        _mainWindow.Closing += (_, args) =>
        {
            if (_isExiting)
            {
                return;
            }

            args.Cancel = true;
            _mainWindow.Hide();
        };

        _trayIconService = new TrayIconService();
        _trayIconService.RestoreRequested += (_, _) =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        };
        _trayIconService.ExitRequested += (_, _) =>
        {
            _isExiting = true;
            Shutdown();
        };
        _trayIconService.Show();

        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        base.OnExit(e);
    }
}
