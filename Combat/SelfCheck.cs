namespace AionSniffer.Combat;

/// <summary>
/// Reproduces two known scenarios against <see cref="DpsCalculator"/> so the formulas can be
/// verified without a live capture: the Gladiator/Zauberer "ALL view" thought experiment that
/// motivated distinguishing DPS from iDPS in the first place, and the real numbers pulled from
/// a public myaion.eu boss-fight session (see README) to ground-truth the iDPS formula.
/// </summary>
public static class SelfCheck
{
    private const int GladiatorId = 1;
    private const int ZaubererId = 2;
    private const int BossId = 100;

    public static bool Run()
    {
        bool ok = true;
        ok &= RunGladiatorVsZaubererScenario();
        ok &= RunMyAionReplayScenario();
        return ok;
    }

    /// <summary>
    /// Gladiator hits a training dummy every second for 300s (constant weaving); Zauberer hits
    /// theirs only every 60s with a big nuke. Demonstrates why "DPS" (ALL view, wall-clock) alone
    /// is misleading, and what "active-only" tries (and, for a lone isolated hit, fails) to fix.
    /// </summary>
    private static bool RunGladiatorVsZaubererScenario()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<DamageEvent>();

        for (int i = 0; i < 300; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i), GladiatorId, BossId, 1000, false));
        }

        for (int i = 0; i < 5; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i * 60), ZaubererId, BossId, 50_000, false));
        }

        double gladWall = DpsCalculator.AllDpsWallClock(events, GladiatorId);
        double zaubWall = DpsCalculator.AllDpsWallClock(events, ZaubererId);
        double? gladActive = DpsCalculator.AllDpsActiveOnly(events, GladiatorId, TimeSpan.FromSeconds(3));
        double? zaubActive = DpsCalculator.AllDpsActiveOnly(events, ZaubererId, TimeSpan.FromSeconds(3));

        Console.WriteLine("[selftest] Gladiator/Zauberer ALL-view scenario:");
        Console.WriteLine($"  Gladiator: wallclock={gladWall:F1} active={Fmt(gladActive)} (300 hits x 1000 dmg, 1s apart)");
        Console.WriteLine($"  Zauberer:  wallclock={zaubWall:F1} active={Fmt(zaubActive)} (5 hits x 50000 dmg, 60s apart)");

        // Expectation: on a raw wall-clock "ALL" reading the bursty Zauberer is NOT obviously
        // behind the sustained Gladiator (~1042 vs ~1003) -- this is the exact unfairness the
        // user's original example was about.
        bool wallClockLooksUnfair = zaubWall > gladWall * 0.9;

        // Expectation: with a 3s idle threshold, every one of the Gladiator's 1s gaps counts
        // (constant activity), so a real rate comes out. Every one of the Zauberer's 60s gaps
        // is excluded outright -- there's no gap short enough to count, so there is no active
        // time to divide by. AllDpsActiveOnly returns null for that rather than the raw damage
        // total (an earlier version did that, and it reads as a UI bug -- "250,000 iDPS" with no
        // context looks broken, not "undefined"; a real build would show "n/a" here instead).
        bool gladiatorActiveIsReasonable = gladActive is double g && g > 900 && g < 1100;
        bool zaubererActiveIsUndefined = zaubActive is null;

        Console.WriteLine($"  -> wall-clock makes the bursty caster look competitive: {wallClockLooksUnfair}");
        Console.WriteLine($"  -> active-only correctly rates the sustained gladiator: {gladiatorActiveIsReasonable}");
        Console.WriteLine($"  -> active-only correctly reports \"undefined\" for the isolated-burst caster: {zaubererActiveIsUndefined}");

        return wallClockLooksUnfair && gladiatorActiveIsReasonable && zaubererActiveIsUndefined;
    }

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("F1") : "n/a";

    /// <summary>
    /// Replays the real numbers from myaion.eu's public session /PvESession/1716393: player
    /// "Strohmie" dealt 84,360,277 damage to one boss at a displayed rate of 438,288. Feeding
    /// synthetic hits spanning the implied ~192.48s duration should reproduce that rate via
    /// <see cref="DpsCalculator.TargetIDps"/>.
    /// </summary>
    private static bool RunMyAionReplayScenario()
    {
        const long totalDamage = 84_360_277;
        const int expectedIDps = 438_288;
        double impliedDurationSeconds = (double)totalDamage / expectedIDps; // ~192.48s

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const int hitCount = 30;
        long perHit = totalDamage / hitCount;
        long remainder = totalDamage - perHit * hitCount;

        var events = new List<DamageEvent>();
        for (int i = 0; i < hitCount; i++)
        {
            double t = i * impliedDurationSeconds / (hitCount - 1);
            long amount = perHit + (i == hitCount - 1 ? remainder : 0);
            events.Add(new DamageEvent(start.AddSeconds(t), GladiatorId, BossId, amount, false));
        }

        double? iDps = DpsCalculator.TargetIDps(events, BossId, GladiatorId);
        Console.WriteLine("[selftest] myaion.eu replay (Strohmie vs. boss, /PvESession/1716393):");
        Console.WriteLine($"  expected iDPS={expectedIDps}, computed iDPS={Fmt(iDps)}");

        bool matches = iDps is double v && Math.Abs(v - expectedIDps) < 1.0;
        Console.WriteLine($"  -> matches within rounding: {matches}");
        return matches;
    }
}
