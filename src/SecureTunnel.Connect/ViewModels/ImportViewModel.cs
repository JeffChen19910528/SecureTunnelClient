using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SecureTunnel.Client.Core.Client;
using SecureTunnel.Client.Core.Configuration;

namespace SecureTunnel.Connect.ViewModels;

/// <summary>
/// Drives the secure configuration import flow. Reads the selected file's
/// content (never trusts the extension alone - the content is what gets
/// parsed/validated server-side), sends it to
/// <see cref="IPrivilegedClientService.ValidateConfigurationAsync"/>, and
/// exposes the result for display. Never auto-issues a connect after a
/// successful import - that remains an explicit, separate user action.
/// </summary>
public sealed class ImportViewModel : INotifyPropertyChanged
{
    private readonly IPrivilegedClientService _clientService;
    private ConfigurationSummary? _lastImportedSummary;
    private IReadOnlyList<string> _validationIssues = [];
    private bool _isImporting;

    public ImportViewModel(IPrivilegedClientService clientService)
    {
        _clientService = clientService;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ConfigurationSummary>? ImportSucceeded;

    /// <summary>
    /// Human-readable validation problems. Derived only from the server's
    /// user-safe message - never from raw configuration text, which could
    /// contain a private key.
    /// </summary>
    public IReadOnlyList<string> ValidationIssues
    {
        get => _validationIssues;
        private set
        {
            _validationIssues = value;
            OnPropertyChanged();
        }
    }

    public ConfigurationSummary? LastImportedSummary
    {
        get => _lastImportedSummary;
        private set
        {
            _lastImportedSummary = value;
            OnPropertyChanged();
        }
    }

    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            _isImporting = value;
            OnPropertyChanged();
        }
    }

    public async Task ImportConfigurationAsync(string filePath, CancellationToken cancellationToken)
    {
        IsImporting = true;
        ValidationIssues = [];

        try
        {
            string rawText;
            try
            {
                rawText = await File.ReadAllTextAsync(filePath, cancellationToken);
            }
            catch (IOException)
            {
                ValidationIssues = ["The selected file could not be read."];
                return;
            }
            catch (UnauthorizedAccessException)
            {
                ValidationIssues = ["The selected file could not be read."];
                return;
            }

            var result = await _clientService.ValidateConfigurationAsync(rawText, cancellationToken);

            if (!result.Success || result.Configuration is null)
            {
                ValidationIssues = [result.UserSafeMessage ?? "The configuration is invalid."];
                return;
            }

            LastImportedSummary = result.Configuration;
            ImportSucceeded?.Invoke(this, result.Configuration);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
