using System.Diagnostics;
using SecureTunnel.Client.Infrastructure.WireGuard;

namespace SecureTunnel.Client.Infrastructure.Tests.WireGuard;

/// <summary>
/// Real-process tests (not fakes) - spawns a short-lived, benign child
/// process to prove the runner actually terminates it on cancellation.
/// Achievable unelevated on this development machine.
/// </summary>
public class WireGuardProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ValidExecutable_ReturnsExitCodeAndOutput()
    {
        var runner = new WireGuardProcessRunner();

        var result = await runner.RunAsync("cmd.exe", ["/c", "echo hello"], CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_InvalidExecutablePath_ReturnsStartedFalse_NeverThrows()
    {
        var runner = new WireGuardProcessRunner();

        var result = await runner.RunAsync(
            Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.exe"),
            [],
            CancellationToken.None);

        Assert.False(result.Started);
    }

    [Fact]
    public async Task RunAsync_CancelledMidRun_KillsTheProcess()
    {
        var runner = new WireGuardProcessRunner();
        using var cts = new CancellationTokenSource();

        // "ping" (unlike "timeout") does not require an interactive
        // console, so it reliably keeps running under redirected I/O.
        var runTask = runner.RunAsync("ping.exe", ["-n", "30", "127.0.0.1"], cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

        // Poll (rather than a single fixed delay) for the OS to actually
        // tear the process down, then confirm no lingering "ping" child
        // process from this test run. A fixed short delay was found to be
        // flaky under system load (Phase C7 real-Windows acceptance run:
        // failed when the full solution test suite ran in parallel,
        // passed in isolation) - polling up to a generous bound removes
        // that flakiness without weakening the assertion itself.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Process[] lingering;
        do
        {
            lingering = Process.GetProcessesByName("ping");
            if (lingering.Length == 0)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        Assert.Empty(lingering);
    }
}
