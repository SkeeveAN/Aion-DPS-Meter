using System.Windows;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>
/// Where windows are put back: a place on a monitor shows the window there, a place on no monitor puts it on the first monitor and holds back saving until the player has decided.
/// </summary>
public static class SelfCheckPlacement
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Window places (second monitor, monitor gone):");
        Rect first = WindowPlacement.Primary();
        var onFirst = new Rect(first.Left + 50, first.Top + 50, 600, 400);
        Rect kept = WindowPlacement.Resolve(onFirst, out bool moved);
        bool here = !moved && kept == onFirst && WindowPlacement.State == WindowPlacement.PlaceState.Normal && WindowPlacement.CanSave;
        Console.WriteLine($"  -> a place on the first monitor is kept as it is and saving stays on: {here}");

        var gone = new Rect(first.Right + 9000, first.Top + 80, 700, 500);
        bool offScreen = !WindowPlacement.IsOnScreen(gone);
        Rect fallback = WindowPlacement.Resolve(gone, out bool movedBack);
        bool onFirstNow = movedBack && fallback.Left >= first.Left && fallback.Top >= first.Top && fallback.Right <= first.Right + 0.5 && fallback.Bottom <= first.Bottom + 0.5 && fallback.Width == 700;
        Console.WriteLine($"  -> a place on no monitor goes to the first monitor (size kept): {offScreen && onFirstNow}");

        bool held = WindowPlacement.State == WindowPlacement.PlaceState.Pending && !WindowPlacement.CanSave;
        Console.WriteLine($"  -> nothing is saved until the player has decided: {held}");

        var huge = new Rect(first.Right + 9000, 0, first.Width + 500, first.Height + 500);
        Rect shrunk = WindowPlacement.Resolve(huge, out _);
        bool fits = shrunk.Width <= first.Width && shrunk.Height <= first.Height;
        Console.WriteLine($"  -> a window larger than the first monitor is made to fit it: {fits}");

        // a window with a few pixels of its title strip still on the monitor can be grabbed; one with none cannot
        bool edge = WindowPlacement.IsOnScreen(new Rect(first.Right - 150, first.Top + 10, 800, 600)) && !WindowPlacement.IsOnScreen(new Rect(first.Right - 5, first.Top + 10, 800, 600));
        Console.WriteLine($"  -> the title strip decides whether a window can still be grabbed: {edge}");
        return here && offScreen && onFirstNow && held && fits && edge;
    }
}
