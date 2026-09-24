using SecureTunnel.Client.Core.Models;
using SecureTunnel.Client.Core.Security;
using SecureTunnel.Client.Core.WireGuard;
using SecureTunnel.Client.Infrastructure.Configuration;
using SecureTunnel.Client.Infrastructure.WireGuard;
using SecureTunnel.Client.TestSupport;

namespace SecureTunnel.Client.Infrastructure.Tests.WireGuard;

public class WireGuardClientServiceTests
{
    private const string TestPeerPublicKey = "R+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=";

    private static ClientConfiguration SampleConfiguration(string clientId) => new()
    {
        ClientId = clientId,
        GatewayName = "Test Gateway",
        GatewayEndpoint = "gateway.example.com:51820",
        InterfaceAddress = "10.0.0.2/32",
        PeerPublicKey = TestPeerPublicKey,
        PeerEndpoint = "gateway.example.com:51820",
        AllowedIPs = ["10.10.0.0/24"]
    };

    [Fact]
    public async Task ConnectAsync_ToolNotInstalled_ReturnsToolNotInstalledOutcome()
    {
        var processRunner = new FakeProcessRunner { ExecutableAvailable = false };
        var store = new FakeClientConfigurationStore();
        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        var result = await service.ConnectAsync(SampleConfiguration("client-1"), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.WireGuardNotInstalled, result.Outcome);
        Assert.False(result.Success);
        Assert.Empty(processRunner.Calls);
    }

    [Fact]
    public async Task ConnectAsync_PermissionDenied_ReturnsPermissionDeniedOutcome()
    {
        var processRunner = new FakeProcessRunner
        {
            ExecutableAvailable = true,
            NextResult = new ProcessRunResult(Started: true, ExitCode: 5, StandardOutput: string.Empty, StandardError: string.Empty)
        };
        var store = new FakeClientConfigurationStore();
        var clientId = "client-2";
        var privateKey = new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        await store.SaveAsync(SampleConfiguration(clientId), privateKey, CancellationToken.None);

        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        var result = await service.ConnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.PermissionDenied, result.Outcome);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ConnectAsync_NeverPassesPrivateKeyAsArgument()
    {
        var processRunner = new FakeProcessRunner { ExecutableAvailable = true };
        var store = new FakeClientConfigurationStore();
        var clientId = "client-3";
        const string keyValue = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var privateKey = new SensitiveString(keyValue);
        await store.SaveAsync(SampleConfiguration(clientId), privateKey, CancellationToken.None);

        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        await service.ConnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        Assert.NotEmpty(processRunner.Calls);
        foreach (var call in processRunner.Calls)
        {
            Assert.DoesNotContain(keyValue, call.Arguments);
        }
    }

    [Fact]
    public async Task ConnectAsync_NeverBuildsConcatenatedShellString()
    {
        var processRunner = new FakeProcessRunner { ExecutableAvailable = true };
        var store = new FakeClientConfigurationStore();
        var clientId = "client-4";
        var privateKey = new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
        await store.SaveAsync(SampleConfiguration(clientId), privateKey, CancellationToken.None);

        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        await service.ConnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        Assert.NotEmpty(processRunner.Calls);
        foreach (var call in processRunner.Calls)
        {
            Assert.All(call.Arguments, arg =>
            {
                Assert.DoesNotContain("&&", arg);
                Assert.DoesNotContain("|", arg);
                Assert.DoesNotContain(";", arg);
            });
        }
    }

    [Fact]
    public async Task ConnectAsync_ProcessCouldNotStart_ReturnsProcessStartFailed()
    {
        var processRunner = new FakeProcessRunner
        {
            ExecutableAvailable = true,
            NextResult = new ProcessRunResult(Started: false, ExitCode: -1, StandardOutput: string.Empty, StandardError: string.Empty)
        };
        var store = new FakeClientConfigurationStore();
        var clientId = "client-5";
        await store.SaveAsync(SampleConfiguration(clientId), new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="), CancellationToken.None);

        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        var result = await service.ConnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.ProcessStartFailed, result.Outcome);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ConnectAsync_OperationTimesOut_KillsProcessAndReturnsConnectionFailed()
    {
        var processRunner = new FakeProcessRunner { ExecutableAvailable = true, ThrowOperationCanceled = true };
        var store = new FakeClientConfigurationStore();
        var clientId = "client-6";
        await store.SaveAsync(SampleConfiguration(clientId), new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="), CancellationToken.None);

        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store, operationTimeout: TimeSpan.FromMilliseconds(1));

        var result = await service.ConnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OperationTimedOut", result.ErrorCode);
        Assert.True(processRunner.KillRequested);
    }

    [Fact]
    public async Task ConnectAsync_OperationTimesOut_TempTunnelFileIsStillCleanedUp()
    {
        var tempDir = Path.GetTempPath();
        var before = Directory.GetFiles(tempDir, "stc-*.conf");

        var processRunner = new FakeProcessRunner { ExecutableAvailable = true, ThrowOperationCanceled = true };
        var store = new FakeClientConfigurationStore();
        var clientId = "client-timeout-cleanup";
        await store.SaveAsync(SampleConfiguration(clientId), new SensitiveString("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="), CancellationToken.None);
        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store, operationTimeout: TimeSpan.FromMilliseconds(1));

        await service.ConnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        var after = Directory.GetFiles(tempDir, "stc-*.conf");
        Assert.Equal(before.Length, after.Length);
    }

    [Fact]
    public async Task DisconnectAsync_TunnelAlreadyAbsent_IsIdempotent_NeverInvokesUninstall()
    {
        var processRunner = new FakeProcessRunner { ExecutableAvailable = true };
        processRunner.ResultsByFirstArgument["/dumptunnelservice"] =
            new ProcessRunResult(Started: true, ExitCode: 0, StandardOutput: string.Empty, StandardError: string.Empty);
        var store = new FakeClientConfigurationStore();
        var clientId = "client-7";

        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        var result = await service.DisconnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(WireGuardOutcome.Disconnected, result.Outcome);
        Assert.DoesNotContain(processRunner.Calls, c => c.Arguments.Count > 0 && c.Arguments[0] == "/uninstalltunnelservice");
    }

    [Fact]
    public async Task DisconnectAsync_UninstallFails_ReturnsDisconnectFailed()
    {
        const string secretLookingLine = "private-key\tAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var processRunner = new FakeProcessRunner { ExecutableAvailable = true };
        processRunner.ResultsByFirstArgument["/dumptunnelservice"] =
            new ProcessRunResult(Started: true, ExitCode: 0, StandardOutput: "interface-line-present", StandardError: string.Empty);
        processRunner.ResultsByFirstArgument["/uninstalltunnelservice"] =
            new ProcessRunResult(Started: true, ExitCode: 1, StandardOutput: string.Empty, StandardError: secretLookingLine);
        var store = new FakeClientConfigurationStore();
        var clientId = "client-8";

        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        var result = await service.DisconnectAsync(SampleConfiguration(clientId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(WireGuardOutcome.DisconnectFailed, result.Outcome);
        Assert.DoesNotContain("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", result.UserSafeMessage);
        Assert.DoesNotContain("private-key", result.UserSafeMessage);
    }

    [Fact]
    public async Task GetStatusAsync_InterfaceOnly_ReturnsInterfaceActive()
    {
        var processRunner = new FakeProcessRunner
        {
            ExecutableAvailable = true,
            NextResult = new ProcessRunResult(Started: true, ExitCode: 0, StandardOutput: "hyCu4faV33FeOhqiRoy9bRSV30qH/XO0K98N3Mg7uD8=\tR+Ufn0v6FOWSyar+vtYvKOiZpXB/QM03LKhqdJUbBZY=\t0\t0", StandardError: string.Empty)
        };
        var store = new FakeClientConfigurationStore();
        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        var result = await service.GetStatusAsync(SampleConfiguration("client-9"), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.InterfaceActive, result.Outcome);
        Assert.Equal(ClientState.InterfaceActive, result.ObservedState);
    }

    [Fact]
    public async Task GetStatusAsync_StatusQueryFails_ReturnsStatusUnavailable_NotDisconnected()
    {
        var processRunner = new FakeProcessRunner
        {
            ExecutableAvailable = true,
            NextResult = new ProcessRunResult(Started: true, ExitCode: 1, StandardOutput: string.Empty, StandardError: "boom")
        };
        var store = new FakeClientConfigurationStore();
        var service = new WireGuardClientService(processRunner, new WireGuardConfigTextParser(), store);

        var result = await service.GetStatusAsync(SampleConfiguration("client-10"), CancellationToken.None);

        Assert.Equal(WireGuardOutcome.StatusUnavailable, result.Outcome);
        Assert.NotEqual(WireGuardOutcome.Disconnected, result.Outcome);
        Assert.False(result.Success);
    }
}
