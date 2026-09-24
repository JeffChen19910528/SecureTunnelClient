using System.Windows;
using Microsoft.Win32;
using SecureTunnel.Connect.ViewModels;

namespace SecureTunnel.Connect;

/// <summary>
/// Shell window. Performs no privileged operations directly and requires
/// no elevation (see app.manifest) - all privileged work is delegated
/// through <see cref="MainViewModel"/>'s injected
/// <see cref="SecureTunnel.Client.Core.Client.IPrivilegedClientService"/>.
/// The file picker below does not trust the file extension alone - the
/// content is what gets parsed/validated server-side
/// (<see cref="ImportViewModel.ImportConfigurationAsync"/>).
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "WireGuard configuration (*.conf)|*.conf|All files (*.*)|*.*",
            Title = "Import WireGuard Configuration"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.Import.ImportConfigurationAsync(dialog.FileName, CancellationToken.None);
        }
    }

    private async void OnRefreshDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.Diagnostics.RefreshAsync(_viewModel.ClientId, CancellationToken.None);
    }
}
