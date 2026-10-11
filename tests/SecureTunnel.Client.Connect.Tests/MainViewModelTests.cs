using System.IO;
using SecureTunnel.Client.Core.Client;
using SecureTunnel.Client.Core.Configuration;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.TestSupport;
using SecureTunnel.Connect.ViewModels;

namespace SecureTunnel.Client.Connect.Tests;

[Collection("Localization")]
public class MainViewModelTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"main-vm-test-{Guid.NewGuid():N}.conf");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public async Task Connect_Success_PollsUntilServiceReportsConnected()
    {
        var fake = new FakePrivilegedClientService
        {
            StatusResult = new PrivilegedClientResult(true, WireGuardOutcome.Connected, null, null, ClientState.Connected)
        };
        var viewModel = new MainViewModel(fake, TimeSpan.FromMilliseconds(10));

        await viewModel.ConnectAsync();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.State != ClientState.Connected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(ClientState.Connected, viewModel.State);
        Assert.Contains("GetStatusAsync", fake.Calls);
    }

    [Fact]
    public async Task Connect_Success_DoesNotClaimConnectedBeforeServiceReportsIt()
    {
        var fake = new FakePrivilegedClientService
        {
            StatusResult = new PrivilegedClientResult(true, WireGuardOutcome.AwaitingHandshake, null, null, ClientState.AwaitingHandshake)
        };
        var viewModel = new MainViewModel(fake, TimeSpan.FromMilliseconds(10));

        await viewModel.ConnectAsync();
        await Task.Delay(100);
        await viewModel.DisconnectAsync();

        Assert.NotEqual(ClientState.Connected, viewModel.State);
    }

    [Fact]
    public async Task Disconnect_Success_ShowsDisconnectedAndStopsPolling()
    {
        var fake = new FakePrivilegedClientService
        {
            StatusResult = new PrivilegedClientResult(true, WireGuardOutcome.Connected, null, null, ClientState.Connected)
        };
        var viewModel = new MainViewModel(fake, TimeSpan.FromMilliseconds(10));
        await viewModel.ConnectAsync();
        await Task.Delay(100);

        await viewModel.DisconnectAsync();
        var callsAtDisconnect = fake.Calls.Count;
        await Task.Delay(150);

        Assert.Equal(ClientState.Disconnected, viewModel.State);
        Assert.Equal(callsAtDisconnect, fake.Calls.Count);
    }

    [Fact]
    public async Task Import_RaisesChangeNotificationsForConfigurationSummaryTexts()
    {
        await File.WriteAllTextAsync(_tempFile, "text");
        var fake = new FakePrivilegedClientService
        {
            ValidateConfigurationResult = new PrivilegedClientResult(true, WireGuardOutcome.Unknown, null, null, null,
                new ConfigurationSummary("client-x", "gw:51820", "10.0.0.2/32", ["10.10.0.0/24"]))
        };
        var viewModel = new MainViewModel(fake, TimeSpan.FromMilliseconds(10));
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await viewModel.Import.ImportConfigurationAsync(_tempFile, CancellationToken.None);

        Assert.Contains(nameof(MainViewModel.ClientIdText), raised);
        Assert.Contains(nameof(MainViewModel.GatewayEndpointText), raised);
        Assert.Contains(nameof(MainViewModel.TunnelAddressText), raised);
        Assert.Contains(nameof(MainViewModel.AllowedIPsLine), raised);
    }

    [Fact]
    public async Task ClientId_FlowsFromImport_IntoSubsequentOperations()
    {
        await File.WriteAllTextAsync(_tempFile, "text");
        var fake = new FakePrivilegedClientService
        {
            ValidateConfigurationResult = new PrivilegedClientResult(true, WireGuardOutcome.Unknown, null, null, null,
                new ConfigurationSummary("client-from-import", "gw:51820", "10.0.0.2/32", ["10.10.0.0/24"]))
        };
        var viewModel = new MainViewModel(fake);

        Assert.Equal(string.Empty, viewModel.ClientId);

        await viewModel.Import.ImportConfigurationAsync(_tempFile, CancellationToken.None);
        Assert.Equal("client-from-import", viewModel.ClientId);

        await viewModel.ConnectAsync();

        Assert.Contains(fake.Calls, call => call == nameof(fake.ConnectAsync));
    }

    [Fact]
    public void CurrentConfiguration_InitiallyNull_HasConfigurationFalse()
    {
        var viewModel = new MainViewModel(new FakePrivilegedClientService());

        Assert.Null(viewModel.CurrentConfiguration);
        Assert.False(viewModel.HasConfiguration);
    }

    [Fact]
    public async Task ConnectAsync_ServiceUnavailable_StatusTextDistinguishesIt()
    {
        var fake = new FakePrivilegedClientService
        {
            ConnectResult = new PrivilegedClientResult(false, WireGuardOutcome.ServiceUnavailable, "ServiceUnavailable", "The service could not be reached.", null)
        };
        var viewModel = new MainViewModel(fake);

        await viewModel.ConnectAsync();

        Assert.Contains("Service unavailable", viewModel.StatusText);
    }

    [Fact]
    public async Task ConnectAsync_WireGuardNotInstalled_StatusTextDistinguishesIt()
    {
        var fake = new FakePrivilegedClientService
        {
            ConnectResult = new PrivilegedClientResult(false, WireGuardOutcome.WireGuardNotInstalled, "WireGuardNotInstalled", null, null)
        };
        var viewModel = new MainViewModel(fake);

        await viewModel.ConnectAsync();

        Assert.Contains("WireGuard engine is missing", viewModel.StatusText);
    }

    [Fact]
    public async Task ConnectAsync_ObservedStateInterfaceActive_NeverDisplaysConnected()
    {
        var fake = new FakePrivilegedClientService
        {
            ConnectResult = new PrivilegedClientResult(true, WireGuardOutcome.Unknown, null, null, ClientState.InterfaceActive)
        };
        var viewModel = new MainViewModel(fake);

        await viewModel.ConnectAsync();

        Assert.Equal(ClientState.InterfaceActive, viewModel.State);
        Assert.NotEqual(ClientState.Connected, viewModel.State);
    }

    [Fact]
    public async Task ConnectAsync_ObservedStateAwaitingHandshake_NeverDisplaysConnected()
    {
        var fake = new FakePrivilegedClientService
        {
            ConnectResult = new PrivilegedClientResult(true, WireGuardOutcome.Unknown, null, null, ClientState.AwaitingHandshake)
        };
        var viewModel = new MainViewModel(fake);

        await viewModel.ConnectAsync();

        Assert.Equal(ClientState.AwaitingHandshake, viewModel.State);
        Assert.NotEqual(ClientState.Connected, viewModel.State);
    }

    [Fact]
    public async Task RefreshStatusAsync_ObservedStateConnected_OnlyDisplayedAfterExplicitHandshakeOutcome()
    {
        var fake = new FakePrivilegedClientService
        {
            StatusResult = new PrivilegedClientResult(true, WireGuardOutcome.Connected, null, null, ClientState.Connected)
        };
        var viewModel = new MainViewModel(fake);

        Assert.Equal(ClientState.NotConfigured, viewModel.State);

        await viewModel.RefreshStatusAsync();

        Assert.Equal(ClientState.Connected, viewModel.State);
    }

    [Fact]
    public async Task ConnectAsync_TimeoutOutcome_StatusTextDoesNotClaimConnected()
    {
        var fake = new FakePrivilegedClientService
        {
            ConnectResult = new PrivilegedClientResult(false, WireGuardOutcome.ConnectionFailed, "OperationTimedOut", "Connecting timed out.", null)
        };
        var viewModel = new MainViewModel(fake);

        await viewModel.ConnectAsync();

        Assert.Contains("timed out", viewModel.StatusText);
        Assert.NotEqual(ClientState.Connected, viewModel.State);
    }

    [Fact]
    public async Task DisconnectAsync_CalledRepeatedly_RemainsSafe_NeverThrows()
    {
        var fake = new FakePrivilegedClientService
        {
            DisconnectResult = new PrivilegedClientResult(true, WireGuardOutcome.Disconnected, null, null, ClientState.Disconnected)
        };
        var viewModel = new MainViewModel(fake);

        await viewModel.DisconnectAsync();
        await viewModel.DisconnectAsync();
        await viewModel.DisconnectAsync();

        Assert.Equal(ClientState.Disconnected, viewModel.State);
        Assert.Equal(3, fake.Calls.Count(c => c == nameof(fake.DisconnectAsync)));
    }
}
