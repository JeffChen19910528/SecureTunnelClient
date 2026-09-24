using System.IO;
using SecureTunnel.Client.Core.Client;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.TestSupport;
using SecureTunnel.Connect.ViewModels;

namespace SecureTunnel.Client.Connect.Tests;

public class ImportViewModelTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"import-test-{Guid.NewGuid():N}.conf");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public async Task ImportConfigurationAsync_ValidConfig_PopulatesSummary_RaisesEvent()
    {
        await File.WriteAllTextAsync(_tempFile, "irrelevant text - the fake never parses it");
        var expectedSummary = new ConfigurationSummary("client-1", "gateway.example.com:51820", "10.0.0.2/32", ["10.10.0.0/24"]);
        var fake = new FakePrivilegedClientService
        {
            ValidateConfigurationResult = new PrivilegedClientResult(true, WireGuardOutcome.Unknown, null, null, null, expectedSummary)
        };
        var viewModel = new ImportViewModel(fake);
        ConfigurationSummary? raised = null;
        viewModel.ImportSucceeded += (_, summary) => raised = summary;

        await viewModel.ImportConfigurationAsync(_tempFile, CancellationToken.None);

        Assert.Equal(expectedSummary, viewModel.LastImportedSummary);
        Assert.Equal(expectedSummary, raised);
        Assert.Empty(viewModel.ValidationIssues);
    }

    [Fact]
    public async Task ImportConfigurationAsync_InvalidConfig_ShowsValidationIssues_NeverThrows()
    {
        await File.WriteAllTextAsync(_tempFile, "malformed");
        var fake = new FakePrivilegedClientService
        {
            ValidateConfigurationResult = new PrivilegedClientResult(false, WireGuardOutcome.InvalidConfiguration, "InvalidConfiguration", "The configuration is invalid.", null)
        };
        var viewModel = new ImportViewModel(fake);

        await viewModel.ImportConfigurationAsync(_tempFile, CancellationToken.None);

        Assert.Single(viewModel.ValidationIssues);
        Assert.Null(viewModel.LastImportedSummary);
    }

    [Fact]
    public async Task ImportConfigurationAsync_NeverAutoConnects()
    {
        await File.WriteAllTextAsync(_tempFile, "text");
        var summary = new ConfigurationSummary("client-1", "gw:51820", "10.0.0.2/32", ["10.10.0.0/24"]);
        var fake = new FakePrivilegedClientService
        {
            ValidateConfigurationResult = new PrivilegedClientResult(true, WireGuardOutcome.Unknown, null, null, null, summary)
        };
        var viewModel = new ImportViewModel(fake);

        await viewModel.ImportConfigurationAsync(_tempFile, CancellationToken.None);

        Assert.DoesNotContain(nameof(fake.ConnectAsync), fake.Calls);
    }

    [Fact]
    public async Task ImportConfigurationAsync_MissingFile_ShowsValidationIssue_NeverThrows()
    {
        var fake = new FakePrivilegedClientService();
        var viewModel = new ImportViewModel(fake);
        var missingPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.conf");

        await viewModel.ImportConfigurationAsync(missingPath, CancellationToken.None);

        Assert.NotEmpty(viewModel.ValidationIssues);
    }

    [Fact]
    public async Task ImportConfigurationAsync_SetsIsImporting_DuringAndAfter()
    {
        await File.WriteAllTextAsync(_tempFile, "text");
        var fake = new FakePrivilegedClientService
        {
            ValidateConfigurationResult = new PrivilegedClientResult(true, WireGuardOutcome.Unknown, null, null, null,
                new ConfigurationSummary("client-1", "gw:51820", "10.0.0.2/32", ["10.10.0.0/24"]))
        };
        var viewModel = new ImportViewModel(fake);

        var task = viewModel.ImportConfigurationAsync(_tempFile, CancellationToken.None);
        await task;

        Assert.False(viewModel.IsImporting);
    }

    [Fact]
    public void ValidationIssues_NeverContainsAKeyShapedToken()
    {
        var viewModel = new ImportViewModel(new FakePrivilegedClientService());

        Assert.All(viewModel.ValidationIssues, issue => Assert.DoesNotMatch(@"[A-Za-z0-9+/]{42,44}=", issue));
    }
}
