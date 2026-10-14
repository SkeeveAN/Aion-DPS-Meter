using System.Windows.Media;
using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The fight clock of the boss bar: it starts with the game's combat signal (also when someone only stood too close), not with the first hit.</summary>
public static class SelfCheckFightClock
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Fight clock of the boss bar:");
        var dir = new Aion2EntityDirectory();
        var t0 = new DateTime(2026, 10, 14, 20, 0, 0);
        dir.NoteEngaged(777, t0);
        dir.NoteEngaged(777, t0.AddSeconds(30)); // the second signal does not move the start
        bool start = dir.EngagedSince(777) == t0 && dir.EngagedSince(778) is null;
        dir.NoteDisengaged(777);
        bool reset = dir.EngagedSince(777) is null;

        bool format = HpBarView.FormatElapsed(TimeSpan.FromSeconds(151)) == "02:31"
            && HpBarView.FormatElapsed(TimeSpan.FromSeconds(5)) == "00:05"
            && HpBarView.FormatElapsed(TimeSpan.FromSeconds(3725)) == "1:02:05";
        bool colours = ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(239)), OverlayPictures.Muted)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(240)), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(279)), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(280)), OverlayPictures.Red);
        bool ok = start && reset && format && colours;
        Console.WriteLine($"  -> start kept at the first signal={start}, cleared when the boss leaves combat={reset}, m:ss and h:mm:ss={format}, yellow from 4:00 and red from 4:40={colours}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
