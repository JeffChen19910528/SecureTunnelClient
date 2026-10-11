using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace SecureTunnel.Client.UiTests;

/// <summary>
/// Real UI automation against the actual WPF executable. These cover items
/// previously Blocked as "requires an interactive Windows session": file
/// dialog, clipboard, and hide-to-tray on close. No Agent is running, so
/// connection operations are expected to report the Agent as unreachable.
/// </summary>
public sealed class MainWindowUiTests
{
    private static readonly string PrivateKey = new string('A', 43) + "=";
    private static readonly string PeerKey = new string('Q', 42) + "E=";

    private static string WriteSampleConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"uitest-{Guid.NewGuid():N}.conf");
        File.WriteAllText(path, string.Join(Environment.NewLine,
            "[Interface]",
            $"PrivateKey = {PrivateKey}",
            "Address = 10.0.0.2/32",
            "",
            "[Peer]",
            $"PublicKey = {PeerKey}",
            "AllowedIPs = 10.10.0.0/24",
            "Endpoint = gateway.example.com:51820"));
        return path;
    }

    private static string AllText(Window window) =>
        string.Join("\n", window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Select(e => e.Name));

    private static string? WaitForText(Window window, string expected) =>
        Retry.WhileNull(() => AllText(window).Contains(expected) ? AllText(window) : null,
            TimeSpan.FromSeconds(15)).Result;

    [InteractiveFact]
    public void MainWindow_ShowsExpectedControls()
    {
        using var app = new ConnectApp();
        var window = app.MainWindow;

        Assert.Equal("SecureTunnel Connect", window.Title);
        foreach (var name in new[] { "Import Configuration...", "Connect", "Disconnect", "Refresh", "Refresh Diagnostics", "Copy to Clipboard" })
        {
            Assert.NotNull(window.FindFirstDescendant(cf => cf.ByName(name)));
        }
    }

    [InteractiveFact]
    public void LanguageToggle_SwitchesBetweenEnglishAndChinese_AndBack()
    {
        // Toggling persists the choice to ui-language.txt; restore the user's real preference afterwards.
        var prefPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecureTunnel", "Connect", "ui-language.txt");
        var original = File.Exists(prefPath) ? File.ReadAllText(prefPath) : null;
        try
        {
            using var app = new ConnectApp();
            var window = app.MainWindow;

            window.FindFirstDescendant(cf => cf.ByName("中文")).AsButton().Invoke();
            Assert.NotNull(Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByName("匯入設定檔...")), TimeSpan.FromSeconds(10)).Result);
            Assert.Null(window.FindFirstDescendant(cf => cf.ByName("Import Configuration...")));

            window.FindFirstDescendant(cf => cf.ByName("English")).AsButton().Invoke();
            Assert.NotNull(Retry.WhileNull(() => window.FindFirstDescendant(cf => cf.ByName("Import Configuration...")), TimeSpan.FromSeconds(10)).Result);
            Assert.Null(window.FindFirstDescendant(cf => cf.ByName("匯入設定檔...")));
        }
        finally
        {
            if (original is null)
            {
                File.Delete(prefPath);
            }
            else
            {
                File.WriteAllText(prefPath, original);
            }
        }
    }

    [InteractiveFact]
    public void RefreshDiagnostics_WithNoAgent_ReportsAgentUnreachable()
    {
        using var app = new ConnectApp();
        var window = app.MainWindow;

        window.FindFirstDescendant(cf => cf.ByName("Refresh Diagnostics")).AsButton().Invoke();

        Assert.NotNull(WaitForText(window, "Agent reachable: False"));
    }

    [InteractiveFact]
    public void Import_ViaFileDialog_WithNoAgent_ShowsSafeErrorAndNoSecrets()
    {
        var configPath = WriteSampleConfig();
        try
        {
            using var app = new ConnectApp();
            var window = app.MainWindow;

            window.FindFirstDescendant(cf => cf.ByName("Import Configuration...")).AsButton().Invoke();

            var dialog = Retry.WhileNull(() => window.ModalWindows.FirstOrDefault(), TimeSpan.FromSeconds(10)).Result;
            Assert.NotNull(dialog);

            // AutomationId 1148 = the common-dialog file name edit; 1 = Open. Locale-independent.
            // UIA ValuePattern only - never global keyboard input, which would land in whatever window has focus.
            dialog!.FindFirstDescendant(cf => cf.ByAutomationId("1148").And(cf.ByControlType(ControlType.Edit))).AsTextBox().Text = configPath;
            // The Open button is a SplitButton in the common dialog and ignores Invoke; post IDOK to this dialog only.
            PostMessage(dialog.Properties.NativeWindowHandle.Value, 0x0111 /* WM_COMMAND */, (IntPtr)1 /* IDOK */, IntPtr.Zero);

            // Validation is performed by the Agent over the Named Pipe; with no Agent
            // running the UI must surface a user-safe error (after the ~10s pipe connect
            // timeout) and must not show a configuration summary.
            var text = Retry.WhileNull(() =>
                AllText(window).Contains("service is not") ? AllText(window) : null,
                TimeSpan.FromSeconds(40)).Result;
            Assert.NotNull(text);
            Assert.DoesNotContain("Selected configuration", text);
            Assert.DoesNotContain(PrivateKey, text);
            Assert.DoesNotContain(PeerKey, text);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [InteractiveFact]
    public void CopyToClipboard_PutsDiagnosticsOnClipboard_WithoutSecrets()
    {
        using var app = new ConnectApp();
        var window = app.MainWindow;

        var sentinel = "ui-test-sentinel-" + Guid.NewGuid().ToString("N");
        RunSta(() => System.Windows.Clipboard.SetText(sentinel));

        window.FindFirstDescendant(cf => cf.ByName("Refresh Diagnostics")).AsButton().Invoke();
        Assert.NotNull(WaitForText(window, "GetStatus"));
        window.FindFirstDescendant(cf => cf.ByName("Copy to Clipboard")).AsButton().Invoke();

        var clip = Retry.WhileNull(() =>
        {
            var t = RunSta(() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "");
            return t != sentinel && t.Length > 0 ? t : null;
        }, TimeSpan.FromSeconds(10)).Result;

        Assert.NotNull(clip);
        Assert.DoesNotContain(PrivateKey, clip);
    }

    [InteractiveFact]
    public void ClosingWindow_HidesToTray_AndProcessKeepsRunning()
    {
        using var app = new ConnectApp();
        var window = app.MainWindow;

        window.Close();
        Thread.Sleep(1500);

        Assert.False(app.Application.HasExited, "Closing the window must hide to tray, not exit.");
        var stillVisible = app.Automation.GetDesktop()
            .FindAllChildren(cf => cf.ByName("SecureTunnel Connect").And(cf.ByControlType(ControlType.Window)))
            .Any(w => !w.Properties.IsOffscreen.ValueOrDefault);
        Assert.False(stillVisible);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static void RunSta(Action action) => RunSta<object?>(() => { action(); return null; });

    private static T RunSta<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); } catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
        return result;
    }
}
