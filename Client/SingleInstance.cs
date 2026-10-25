using System.Runtime.InteropServices;
using System.Windows;

namespace AionDPS;

/// <summary>
/// One meter window per user session: a second start does not open a second window (two captures
/// would double every number and fight over the settings file) but asks the first one to come to
/// the front and ends. The first instance owns a named mutex and waits on a named event; the
/// second one sets that event. "Local\" names are per logon session, so an elevated and a plain
/// process of the same user still see each other.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\AionDPS.SingleInstance";
    private const string ShowEventName = @"Local\AionDPS.ShowWindow";
    private const uint AsfwAny = 0xFFFFFFFF;

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    /// <summary>
    /// True when this process is the first meter and may open its window. False: another one runs
    /// and has been told to show itself. An update restart starts the new build while the old one
    /// is still shutting down, hence the short wait before giving up.
    /// </summary>
    public static bool TryBecomeFirst()
    {
        try
        {
            _mutex = new Mutex(false, MutexName);
            bool owned;
            try
            {
                owned = _mutex.WaitOne(TimeSpan.FromSeconds(4));
            }
            catch (AbandonedMutexException)
            {
                owned = true; // the previous meter crashed without releasing it: it is gone, we are first
            }

            if (owned)
            {
                _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                return true;
            }

            // The user just started this process, so Windows lets it hand the foreground right over.
            AllowSetForegroundWindow(AsfwAny);
            using var existing = EventWaitHandle.OpenExisting(ShowEventName);
            existing.Set();
            return false;
        }
        catch (Exception)
        {
            // No mutex or event to be had (rights, a very old build running): better two windows than none.
            return true;
        }
    }

    /// <summary>Brings the window to the front whenever a second start asks for it.</summary>
    public static void ListenForShow(Window window)
    {
        if (_showEvent is not { } signal)
        {
            return;
        }

        new Thread(() =>
        {
            while (signal.WaitOne())
            {
                window.Dispatcher.BeginInvoke(() => BringToFront(window));
            }
        }) { IsBackground = true, Name = "single-instance" }.Start();
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        // Activate alone only flashes the taskbar button when another program (the game) is in front.
        bool topmost = window.Topmost;
        window.Topmost = true;
        window.Topmost = topmost;
        window.Focus();
    }
}
