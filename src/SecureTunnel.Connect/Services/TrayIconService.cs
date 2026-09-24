using System.Windows.Forms;
using SecureTunnel.Client.Core.Models;

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

    public event EventHandler? RestoreRequested;

    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Restore", null, (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty));
        contextMenu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "SecureTunnel Connect",
            Visible = false,
            ContextMenuStrip = contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show() => _notifyIcon.Visible = true;

    public void UpdateState(ClientState state)
    {
        _notifyIcon.Text = $"SecureTunnel Connect - {state}";
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
