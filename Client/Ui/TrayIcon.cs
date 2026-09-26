using System.Windows;
using System.Windows.Forms;

namespace AionDPS.Ui;

/// <summary>
/// "Minimize to system tray" (App menu) - hides the window and shows a tray icon with a small
/// context menu (Show/Close) instead, since a hidden window otherwise has no way back at all.
/// WPF has no tray-icon API of its own; NotifyIcon (System.Windows.Forms, enabled via
/// UseWindowsForms in the .csproj purely for this) is the standard way every WPF app gets one.
/// Constructed lazily (see MainWindow.OnMinimizeToTrayClicked) rather than at startup - no reason
/// to touch Shell_NotifyIcon at all for a user who never uses this.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Window _window;

    public TrayIcon(Window window, string iconPath, string tooltip, string showText, string closeText)
    {
        _window = window;
        _icon = new NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconPath),
            Text = tooltip,
            Visible = false,
        };
        _icon.DoubleClick += (_, _) => Restore();

        var menu = new ContextMenuStrip();
        menu.Items.Add(showText, null, (_, _) => Restore());
        menu.Items.Add(new ToolStripSeparator());
        // Closes the real window (fires OnClosing/OnClosed normally, same as the titlebar's own
        // close button) rather than a bare Environment.Exit - a hidden window is still fully
        // "open" as far as WPF is concerned, so Close() here works exactly like it would if the
        // window were visible.
        menu.Items.Add(closeText, null, (_, _) => _window.Close());
        _icon.ContextMenuStrip = menu;
    }

    public void MinimizeToTray()
    {
        _icon.Visible = true;
        _window.Hide();
    }

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _icon.Visible = false;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
