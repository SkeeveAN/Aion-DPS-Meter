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

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>True while Windows' own screenshot tool (Win+Shift+S) has the screen: raising the
    /// meter above its dimmed overlay would put the meter in the way of the selection.</summary>
    private static bool ScreenClippingActive()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName is "ScreenClippingHost" or "SnippingTool" or "ScreenSketch";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010;

    /// <summary>
    /// Puts an always-on-top window back at the front of the topmost band. A game in borderless
    /// full screen can bring itself to the front of that band when it takes the focus back, and
    /// the meter then sat behind it until switched off and on. No move, no resize, no activation:
    /// the game keeps the keyboard. (An exclusive full-screen game shows no other window at all;
    /// only its windowed or borderless mode lets an overlay through.)
    /// </summary>
    public static void KeepOnTop(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && !ScreenClippingActive())
        {
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }
    }

    private const int WM_DWMSENDICONICTHUMBNAIL = 0x0323;
    private const int WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;
    private const int DWMWA_FORCE_ICONIC_REPRESENTATION = 7;
    private const int DWMWA_HAS_ICONIC_BITMAP = 10;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hbmp, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hbmp, IntPtr ptClient, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private readonly IntPtr _handle;
    private HwndSource? _source;
    private Func<int, int, System.Drawing.Bitmap?>? _renderPreview;

    /// <summary>Window must already be shown (have a native handle) before constructing this.</summary>
    public NativeOverlay(Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("NativeOverlay requires the window to already have a native handle (construct after Show()/SourceInitialized).");
        }
    }

    /// <summary>
    /// While the window is click-through and transparent (Hide UI) Windows has nothing to show for it
    /// in Alt+Tab and the taskbar preview - an empty dark tile. With a renderer, the window instead
    /// hands Windows its own picture (<c>DwmSetIconicThumbnail</c>) of what the overlay shows, drawn
    /// on request at the size Windows asks for. Without one (null) the normal live preview returns.
    /// </summary>
    public void SetIconicPreview(Func<int, int, System.Drawing.Bitmap?>? render)
    {
        _renderPreview = render;
        int on = render is null ? 0 : 1;
        DwmSetWindowAttribute(_handle, DWMWA_FORCE_ICONIC_REPRESENTATION, ref on, sizeof(int));
        DwmSetWindowAttribute(_handle, DWMWA_HAS_ICONIC_BITMAP, ref on, sizeof(int));
        if (render is not null && _source is null)
        {
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(WndProc);
        }

        if (render is not null)
        {
            DwmInvalidateIconicBitmaps(_handle);
        }
    }

    /// <summary>Tells Windows the picture is out of date; it asks for a new one the next time it needs it.</summary>
    public void InvalidatePreview()
    {
        if (_renderPreview is not null)
        {
            DwmInvalidateIconicBitmaps(_handle);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_renderPreview is null || (msg != WM_DWMSENDICONICTHUMBNAIL && msg != WM_DWMSENDICONICLIVEPREVIEWBITMAP))
        {
            return IntPtr.Zero;
        }

        // For the thumbnail the high word of lParam is the largest width, the low word the largest
        // height Windows will show; the live preview is wanted at the window's own size.
        int maxWidth = msg == WM_DWMSENDICONICTHUMBNAIL ? (int)(((long)lParam >> 16) & 0xFFFF) : 0;
        int maxHeight = msg == WM_DWMSENDICONICTHUMBNAIL ? (int)((long)lParam & 0xFFFF) : 0;
        using System.Drawing.Bitmap? bitmap = _renderPreview(maxWidth, maxHeight);
        if (bitmap is not null)
        {
            IntPtr hbitmap = bitmap.GetHbitmap(System.Drawing.Color.Black);
            try
            {
                if (msg == WM_DWMSENDICONICTHUMBNAIL)
                {
                    DwmSetIconicThumbnail(_handle, hbitmap, 0);
                }
                else
                {
                    DwmSetIconicLivePreviewBitmap(_handle, hbitmap, IntPtr.Zero, 0);
                }
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }

        handled = true;
        return IntPtr.Zero;
    }

    public void SetClickThrough(bool enabled)
    {
        int style = GetWindowLong(_handle, GWL_EXSTYLE);
        style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~(WS_EX_TRANSPARENT | WS_EX_LAYERED);
        SetWindowLong(_handle, GWL_EXSTYLE, style);
    }

    public void Dispose()
    {
        _source?.RemoveHook(WndProc);
    }
}
