using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>
/// Win32 interop backing "Hide UI": makes the window click-through (mouse input passes to
/// whatever is behind it, e.g. the game) so the overlay doesn't steal focus/clicks. Turning it
/// back off needs a global hotkey (see GlobalHotkeys), because a click-through window makes its own
/// "restore" button unreachable by definition.
/// </summary>
internal sealed class NativeOverlay : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly IntPtr _handle;

    /// <summary>Window must already be shown (have a native handle) before constructing this.</summary>
    public NativeOverlay(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("NativeOverlay requires the window to already have a native handle (construct after Show()/SourceInitialized).");
        }
    }

    public void SetClickThrough(bool enabled)
    {
        int style = GetWindowLong(_handle, GWL_EXSTYLE);
        style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~(WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetWindowLong(_handle, GWL_EXSTYLE, style);
    }

    public void Dispose()
    {
    }
}
