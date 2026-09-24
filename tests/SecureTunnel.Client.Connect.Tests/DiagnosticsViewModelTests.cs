using System.Text.RegularExpressions;
using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.TestSupport;
using SecureTunnel.Connect.ViewModels;

namespace SecureTunnel.Client.Connect.Tests;

public class DiagnosticsViewModelTests
{
    private static readonly Regex KeyShapedToken = new(@"[A-Za-z0-9+/]{42,44}=", RegexOptions.Compiled);

    [Fact]
    public async Task RefreshAsync_PopulatesAgentAvailability_FromStatusResult()
    {
        var fake = new FakePrivilegedClientService
        {
            StatusResult = new SecureTunnel.Client.Core.Client.PrivilegedClientResult(true, WireGuardOutcome.Connected, null, null, SecureTunnel.Client.Core.Models.ClientState.Connected)
        };
        var viewModel = new DiagnosticsViewModel(fake);

        await viewModel.RefreshAsync("client-1", CancellationToken.None);

        Assert.True(viewModel.AgentAvailable);
        Assert.True(viewModel.WireGuardExecutableAvailable);
    }

    [Fact]
    public async Task RefreshAsync_ServiceUnavailable_ReportsAgentNotAvailable()
    {
        var fake = new FakePrivilegedClientService
        {
            StatusResult = new SecureTunnel.Client.Core.Client.PrivilegedClientResult(false, WireGuardOutcome.ServiceUnavailable, "ServiceUnavailable", "unreachable", null)
        };
        var viewModel = new DiagnosticsViewModel(fake);

        await viewModel.RefreshAsync("client-1", CancellationToken.None);

        Assert.False(viewModel.AgentAvailable);
    }

    [Fact]
    public async Task BuildClipboardText_NeverContainsAKeyShapedToken()
    {
        var fake = new FakePrivilegedClientService
        {
            StatusResult = new SecureTunnel.Client.Core.Client.PrivilegedClientResult(true, WireGuardOutcome.Connected, null, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", SecureTunnel.Client.Core.Models.ClientState.Connected)
        };
        var viewModel = new DiagnosticsViewModel(fake);
        await viewModel.RefreshAsync("client-1", CancellationToken.None);

        var text = viewModel.BuildClipboardText();

        // Even if a caller (bug or otherwise) put a key-shaped value into
        // UserSafeMessage, DiagnosticResultFactory.Sanitize strips it
        // before it becomes part of the DiagnosticResult stored here.
        Assert.DoesNotMatch(KeyShapedToken, text);
    }
}
