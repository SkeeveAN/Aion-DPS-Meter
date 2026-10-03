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

        menu.Renderer = new ThemedMenuRenderer();
        menu.ShowImageMargin = false;
        _icon.ContextMenuStrip = menu;
    }

    private static System.Drawing.Color ThemeColor(string key, System.Drawing.Color fallback)
    {
        if (System.Windows.Application.Current?.Resources[key] is System.Windows.Media.Color c)
        {
            return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        }

        return fallback;
    }

    /// <summary>The menu in the meter's colours (panel, border, text, accent) instead of the system's
    /// light one; the colours are read from the current theme each time the menu is built.</summary>
    private sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new ThemedColors())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = ThemeColor("Color.Text", System.Drawing.Color.White);
            base.OnRenderItemText(e);
        }
    }

    private sealed class ThemedColors : ProfessionalColorTable
    {
        private readonly System.Drawing.Color _panel = ThemeColor("Color.Panel", System.Drawing.Color.FromArgb(0x15, 0x25, 0x32));
        private readonly System.Drawing.Color _border = ThemeColor("Color.Border", System.Drawing.Color.FromArgb(0x29, 0x45, 0x57));
        private readonly System.Drawing.Color _title = ThemeColor("Color.TitleBar", System.Drawing.Color.FromArgb(0x1b, 0x31, 0x42));
        private readonly System.Drawing.Color _accent = ThemeColor("Color.Accent", System.Drawing.Color.FromArgb(0xff, 0xba, 0x42));

        public override System.Drawing.Color ToolStripDropDownBackground => _panel;
        public override System.Drawing.Color ImageMarginGradientBegin => _panel;
        public override System.Drawing.Color ImageMarginGradientMiddle => _panel;
        public override System.Drawing.Color ImageMarginGradientEnd => _panel;
        public override System.Drawing.Color MenuBorder => _border;
        public override System.Drawing.Color MenuItemBorder => _accent;
        public override System.Drawing.Color MenuItemSelected => _title;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => _title;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => _title;
        public override System.Drawing.Color SeparatorDark => _border;
        public override System.Drawing.Color SeparatorLight => _border;
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
