using System.Windows;
using System.Windows.Forms;

namespace AionDPS.Ui;

/// <summary>
/// The icon in the Windows notification area, shown for as long as the meter runs: a double click
/// brings the window back, a right click opens a small menu (supplied by the caller). The window can
/// also be put away into it ("Minimize to system tray" in the App menu), since a hidden window would
/// otherwise have no way back. WPF has no tray-icon API of its own; NotifyIcon (System.Windows.Forms,
/// enabled via UseWindowsForms in the .csproj purely for this) is the standard way every WPF app gets one.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Window _window;

    public TrayIcon(Window window, string iconPath, string tooltip, IEnumerable<(string? Text, Action? Action)> menuItems)
    {
        _window = window;
        _icon = new NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconPath),
            Text = tooltip,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => Restore();

        var menu = new ContextMenuStrip();
        foreach (var (text, action) in menuItems)
        {
            if (text is null)
            {
                menu.Items.Add(new ToolStripSeparator());
            }
            else
            {
                menu.Items.Add(text, null, (_, _) => action?.Invoke());
            }
        }

        _icon.ContextMenuStrip = menu;
    }

    /// <summary>Puts the window away; the icon stays.</summary>
    public void HideWindow() => _window.Hide();

    /// <summary>Brings the window back from the tray or the taskbar.</summary>
    public void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
