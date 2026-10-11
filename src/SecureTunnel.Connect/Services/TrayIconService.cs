using System.Windows.Forms;
using SecureTunnel.Client.Core.Models;
using SecureTunnel.Connect.Localization;

namespace SecureTunnel.Connect.Services;

/// <summary>
/// Minimal system tray presence via <see cref="System.Windows.Forms.NotifyIcon"/>
/// (in-box with the Windows Desktop SDK via <c>&lt;UseWindowsForms&gt;true&lt;/UseWindowsForms&gt;</c>
/// - chosen over a third-party tray package specifically to avoid adding a
/// new NuGet dependency for one icon). Deliberately has no knowledge of
/// the Agent connection or WireGuard - closing/hiding the window or
/// choosing "Restore" never starts, stops, or disconnects anything; only
/// the explicit "Exit" menu item raises <see cref="ExitRequested"/>.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripItem _restoreItem;
    private readonly ToolStripItem _exitItem;

    public event EventHandler? RestoreRequested;

    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var contextMenu = new ContextMenuStrip();
        _restoreItem = contextMenu.Items.Add(Loc.T("Tray_Restore"), null, (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty));
        _exitItem = contextMenu.Items.Add(Loc.T("Tray_Exit"), null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        Loc.Instance.LanguageChanged += OnLanguageChanged;

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "SecureTunnel Connect",
            Visible = false,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _restoreItem.Text = Loc.T("Tray_Restore");
        _exitItem.Text = Loc.T("Tray_Exit");
    }

    public void Show() => _notifyIcon.Visible = true;

    public void UpdateState(ClientState state)
    {
        _notifyIcon.Text = $"SecureTunnel Connect - {state}";
    }

    public void Dispose()
    {
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
