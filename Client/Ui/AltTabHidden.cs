using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>Keeps an overlay window out of Alt+Tab. ShowInTaskbar=false alone is not enough for
/// borderless topmost windows: Windows still lists them until they carry WS_EX_TOOLWINDOW.</summary>
internal static class AltTabHidden
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void Apply(Window window)
    {
        void Set()
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
                return;
            int style = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, (style | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set();
        else
            window.SourceInitialized += (_, _) => Set();
    }
}
