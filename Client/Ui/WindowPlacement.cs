using System.Windows;

namespace AionDPS.Ui;

/// <summary>
/// Where windows may be put back: a window that was left on a second monitor comes back there, one whose monitor is not connected right now comes back on the
/// first monitor instead. The player is asked once what to do with the saved places then: overwrite them (the windows are saved where they are now) or keep them
/// for the next start (they are not saved while this meter runs). All coordinates are device-independent pixels of the whole desktop, like WPF's Left/Top.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Whether the saved places may be written.</summary>
    public enum PlaceState
    {
        /// <summary>Every saved place was on a monitor that is there.</summary>
        Normal,

        /// <summary>A saved place was not on any monitor: nothing is saved until the player has decided.</summary>
        Pending,

        /// <summary>The player let the saved places be overwritten.</summary>
        Overwrite,

        /// <summary>The player keeps the saved places for the next start: nothing is saved while this meter runs.</summary>
        Keep,
    }

    public static PlaceState State { get; private set; } = PlaceState.Normal;

    /// <summary>The places of windows may be written (no place was lost, or the player chose to overwrite).</summary>
    public static bool CanSave => State is PlaceState.Normal or PlaceState.Overwrite;

    /// <summary>The player chose to overwrite the saved places: the open windows save where they are now.</summary>
    public static event Action? Overwrote;

    private static int _slot;

    /// <summary>The monitors, as the areas their windows may use (the task bar excluded).</summary>
    public static IReadOnlyList<Rect> WorkAreas()
    {
        var list = new List<Rect>();
        double ratio = Ratio();
        foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
        {
            System.Drawing.Rectangle a = screen.WorkingArea;
            list.Add(new Rect(a.X / ratio, a.Y / ratio, a.Width / ratio, a.Height / ratio));
        }

        if (list.Count == 0)
        {
            list.Add(SystemParameters.WorkArea);
        }

        return list;
    }

    /// <summary>The work area of the first (main) monitor.</summary>
    public static Rect Primary()
    {
        double ratio = Ratio();
        System.Windows.Forms.Screen? screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null)
        {
            return SystemParameters.WorkArea;
        }

        System.Drawing.Rectangle a = screen.WorkingArea;
        return new Rect(a.X / ratio, a.Y / ratio, a.Width / ratio, a.Height / ratio);
    }

    /// <summary>Screen pixels per device-independent pixel (the system scaling, 1.25 at 125 %).</summary>
    private static double Ratio()
    {
        double width = SystemParameters.VirtualScreenWidth;
        double ratio = width > 0 ? System.Windows.Forms.SystemInformation.VirtualScreen.Width / width : 1.0;
        return ratio > 0.1 ? ratio : 1.0;
    }

    /// <summary>The title strip of the window can be seen and grabbed on a monitor.</summary>
    public static bool IsOnScreen(Rect window)
    {
        var strip = new Rect(window.X, window.Y, Math.Max(1, window.Width), Math.Min(Math.Max(1, window.Height), 30));
        foreach (Rect area in WorkAreas())
        {
            Rect common = Rect.Intersect(strip, area);
            if (!common.IsEmpty && common.Width >= Math.Min(strip.Width, 120) && common.Height >= Math.Min(strip.Height, 20))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The work area the window mostly lies on (the first monitor when it lies on none): the size the window may have there.</summary>
    public static Rect WorkAreaOf(Rect window)
    {
        Rect best = Primary();
        double bestArea = 0;
        foreach (Rect area in WorkAreas())
        {
            Rect common = Rect.Intersect(window, area);
            double size = common.IsEmpty ? 0 : common.Width * common.Height;
            if (size > bestArea)
            {
                bestArea = size;
                best = area;
            }
        }

        return best;
    }

    /// <summary>
    /// Where a window with the saved place goes: there when a monitor shows it, else on the first monitor (each such window a little lower and to the right of the one
    /// before). A lost place switches to <see cref="PlaceState.Pending"/> until the player decides (see <see cref="AskIfNeeded"/>).
    /// </summary>
    public static Rect Resolve(Rect saved, out bool movedToFirstMonitor)
    {
        movedToFirstMonitor = false;
        if (IsOnScreen(saved))
        {
            return saved;
        }

        movedToFirstMonitor = true;
        if (State == PlaceState.Normal)
        {
            State = PlaceState.Pending;
        }

        Rect area = Primary();
        double width = Math.Min(saved.Width, area.Width);
        double height = Math.Min(saved.Height, area.Height);
        double step = 32 * (_slot++ % 8);
        double left = Math.Min(area.Left + 40 + step, area.Right - width);
        double top = Math.Min(area.Top + 40 + step, area.Bottom - height);
        return new Rect(Math.Max(area.Left, left), Math.Max(area.Top, top), width, height);
    }

    /// <summary>
    /// Asks what to do with the saved places when one was on a monitor that is not there (once per run). Call when the main window is up: the question needs the meter's own
    /// styles. In the test modes without windows nothing is asked and the saved places stay as they are.
    /// </summary>
    public static void AskIfNeeded(Window? owner)
    {
        if (State != PlaceState.Pending)
        {
            return;
        }

        if (MainWindow.Headless)
        {
            State = PlaceState.Keep;
            return;
        }

        var loc = LocalizationManager.Instance;
        Window box = ThemedMessageBox.Build(owner, loc["Placement.OffScreen.Text"], loc["Placement.OffScreen.Title"], MessageBoxButton.YesNo, MessageBoxImage.Question,
            null, null, out ThemedMessageBox.Answer answer, (loc["Placement.OffScreen.Overwrite"], loc["Placement.OffScreen.Keep"], ""));
        box.ShowDialog();

        // closing the box with its X keeps what is saved: nothing is lost by it
        if (answer.Value == MessageBoxResult.Yes)
        {
            State = PlaceState.Overwrite;
            Overwrote?.Invoke();
        }
        else
        {
            State = PlaceState.Keep;
        }
    }
}
