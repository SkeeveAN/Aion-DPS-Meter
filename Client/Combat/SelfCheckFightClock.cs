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
        dir.NoteEngaged(779, t0);
        dir.NoteEngaged(779, t0.AddSeconds(5), exact: true); // the signal with the start time wins over the plain flag
        reset &= dir.EngagedSince(779) == t0.AddSeconds(5);

        bool format = HpBarView.FormatElapsed(TimeSpan.FromSeconds(151)) == "02:31"
            && HpBarView.FormatElapsed(TimeSpan.FromSeconds(5)) == "00:05"
            && HpBarView.FormatElapsed(TimeSpan.FromSeconds(3725)) == "1:02:05";
        bool colours = ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(239)), OverlayPictures.Muted)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(240)), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(279)), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(280)), OverlayPictures.Red);
        bool perBoss = ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(149), 210), OverlayPictures.Muted)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(150), 210), OverlayPictures.Amber)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(190), 210), OverlayPictures.Red)
            && ReferenceEquals(HpBarView.ElapsedBrush(TimeSpan.FromSeconds(430), 480), OverlayPictures.Amber)
            && AionDPS.Aion2.Protocol.Aion2BossRage.SecondsFor(2300582) == 210 && AionDPS.Aion2.Protocol.Aion2BossRage.SecondsFor(1) == 300;
        bool ok = start && reset && format && colours && perBoss;
        Console.WriteLine($"  -> start kept at the first signal={start}, cleared when the boss leaves combat={reset}, m:ss and h:mm:ss={format}, yellow from 4:00 and red from 4:40={colours}, per-boss rage time={perBoss}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
