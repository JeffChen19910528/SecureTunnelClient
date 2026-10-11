using System.IO;
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace SecureTunnel.Client.UiTests;

/// <summary>
/// Launches the real SecureTunnel.Connect.exe (unelevated, no Agent running)
/// and tears it down - killing the process if the test did not exit it.
/// </summary>
public sealed class ConnectApp : IDisposable
{
    public Application Application { get; }

    public UIA3Automation Automation { get; } = new();

    public ConnectApp()
    {
        Application = Application.Launch(CreateStartInfo());
    }

    public Window MainWindow => Application.GetMainWindow(Automation, TimeSpan.FromSeconds(15))
        ?? throw new InvalidOperationException("Main window did not appear.");

    // The UI tests locate controls by their English names, so pin the app language
    // (otherwise a Chinese OS or a saved preference would start it in Chinese).
    private static ProcessStartInfo CreateStartInfo()
    {
        var startInfo = new ProcessStartInfo(FindExecutable());
        startInfo.Environment["SECURETUNNEL_UI_LANGUAGE"] = "en";
        return startInfo;
    }

    private static string FindExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("SECURETUNNEL_CONNECT_EXE");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SecureTunnelClient.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("Repository root not found.");
        }

        var binRoot = Path.Combine(dir.FullName, "src", "SecureTunnel.Connect", "bin");
        return Directory.EnumerateFiles(binRoot, "SecureTunnel.Connect.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();
    }

    public void Dispose()
    {
        try
        {
            if (!Application.HasExited)
            {
                Application.Kill();
            }
        }
        catch (Exception)
        {
            // best-effort cleanup
        }

        Automation.Dispose();
        Application.Dispose();
    }
}
