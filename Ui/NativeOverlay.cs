using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AionSniffer.Ui;

/// <summary>
/// Win32 interop backing "Hide UI": makes the window click-through (mouse input passes to
/// whatever is behind it, e.g. the game) so the overlay doesn't steal focus/clicks, plus a global
/// hotkey (Ctrl+Alt+H) to toggle it back off. The hotkey exists specifically because a
/// click-through window makes its own "restore" button unreachable by definition -- without it,
/// turning Hide UI on would be a one-way trip requiring Alt+F4 or Task Manager to undo.
/// </summary>
internal sealed class NativeOverlay : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA10E; // arbitrary, only needs to be unique within this process
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkH = 0x48;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private readonly IntPtr _handle;

    /// <summary>Fired when the user presses Ctrl+Alt+H, regardless of window focus.</summary>
    public event Action? HotkeyPressed;

    /// <summary>Window must already be shown (have a native handle) before constructing this.</summary>
    public NativeOverlay(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("NativeOverlay requires the window to already have a native handle (construct after Show()/SourceInitialized).");
        _source.AddHook(WndProc);
        RegisterHotKey(_handle, HotkeyId, ModControl | ModAlt, VkH);
    }

    public void SetClickThrough(bool enabled)
    {
        int style = GetWindowLong(_handle, GWL_EXSTYLE);
        style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~(WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetWindowLong(_handle, GWL_EXSTYLE, style);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterHotKey(_handle, HotkeyId);
        _source.RemoveHook(WndProc);
    }
}
