using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>
/// Colors a window's native OS titlebar dark (Windows 10 1809+ / 11) via DWM, for a window that
/// keeps the normal OS chrome (unlike MainWindow/SettingsWindow, which both draw their own via
/// WindowStyle="None" now). Without this, a plain WPF Window's titlebar stays the OS's default
/// light color regardless of the app's own dark theme. Currently unused - SettingsWindow was
/// this class's original reason to exist, but per the user it later got the full custom titlebar
/// treatment instead of just a recolored native one; kept here as a one-line fix ready for
/// whichever of the app's other still-native-chrome dialogs (FightHistoryWindow,
/// PlayerDetailsWindow, PlayerDatabaseWindow) gets asked for next, without redoing this DWM call.
/// </summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Call once the window has a native handle (SourceInitialized or later).</summary>
    public static void Apply(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        int enabled = 1;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));
    }
}
