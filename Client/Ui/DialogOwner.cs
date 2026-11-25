using System.Windows;
using System.Windows.Interop;

namespace AionDPS.Ui;

/// <summary>
/// The window a dialog is put over. A page of the new interface is the content of a window that was never shown (the hub holds its content), and a common dialog
/// (save, open, choose a folder) over such a window fails with "Operation is not valid due to the current state of the object": the dialog gets the window that stands on the screen.
/// </summary>
internal static class DialogOwner
{
    private static bool IsShown(Window? window) => window is { IsVisible: true } && new WindowInteropHelper(window).Handle != IntPtr.Zero;

    /// <summary>The window itself when it is shown, else the active window or the first one that is shown; null when there is none.</summary>
    public static Window? Of(Window? self)
    {
        if (IsShown(self))
        {
            return self;
        }

        IEnumerable<Window> windows = Application.Current?.Windows.OfType<Window>() ?? Enumerable.Empty<Window>();
        return windows.FirstOrDefault(w => w.IsActive && IsShown(w)) ?? windows.FirstOrDefault(IsShown);
    }

    /// <summary>Shows a save, open or folder dialog over the window that is really there (without an owner when none is).</summary>
    public static bool? Show(Microsoft.Win32.CommonDialog dialog, Window? self)
    {
        Window? owner = Of(self);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }
}
