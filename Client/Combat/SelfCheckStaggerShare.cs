namespace AionDPS.Combat;

/// <summary>The stagger share: each fall of a boss's bar is credited to the hit of its packet whose drop it equals, in order.</summary>
public static class SelfCheckStaggerShare
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Stagger share:");
        const int boss = 900;
        var drops = new Dictionary<int, int> { [100] = 5, [200] = 2 };
        DateTime t = new(2026, 10, 31, 12, 0, 0);

        DamageEvent Hit(DateTime at, int source, int skill) => new(at, source, boss, 1000, false, SkillId: skill);
        StaggerEvent Bar(DateTime at, int current) => new(at, boss, current, 1200, false);

        // One packet: player 1 hits with skill 100 (drop 5), player 2 with skill 200 (drop 2), the bar reads twice, in that order.
        // A later packet: player 1 under an amplifying buff (5 becomes 7), a hit that did not count (no reading), and a drop no hit explains.
        var hits = new List<DamageEvent>
        {
            Hit(t, 1, 100), Hit(t, 2, 200),
            Hit(t.AddSeconds(1), 1, 100),
            Hit(t.AddSeconds(2), 2, 200),                                   // counted nowhere: the bar did not move
            Hit(t.AddSeconds(3), 2, 100),
        };
        var readings = new List<StaggerEvent>
        {
            Bar(t.AddMilliseconds(-300), 1200),
            Bar(t, 1195), Bar(t, 1193),
            Bar(t.AddSeconds(1), 1186),                                      // 7 = 5 amplified by half, rounded down
            Bar(t.AddSeconds(3), 1177),                                      // 9: matches nothing (hit of skill 100 drops 5 or 7)
        };
        StaggerShareResult result = StaggerShare.Compute(hits, readings, boss, t.AddSeconds(-5), t.AddSeconds(5), id => drops.GetValueOrDefault(id));
        bool packet = result.BySource.GetValueOrDefault(1) == 5 + 7 && result.BySource.GetValueOrDefault(2) == 2;
        bool totals = result.Total == 5 + 2 + 7 + 9 && result.Assigned == 5 + 2 + 7;

        // A break resets the reading: the refill after it is no drop, a hit during the recharge changes nothing.
        var broken = new List<StaggerEvent> { Bar(t, 30), new(t.AddSeconds(1), boss, 0, 0, true), Bar(t.AddSeconds(7), 1200), Bar(t.AddSeconds(8), 1195) };
        StaggerShareResult afterBreak = StaggerShare.Compute(new[] { Hit(t.AddSeconds(8), 3, 100) }, broken, boss, t.AddSeconds(-5), t.AddSeconds(10), id => drops.GetValueOrDefault(id));
        bool resets = afterBreak.Total == 5 && afterBreak.BySource.GetValueOrDefault(3) == 5;

        // The boss's own start-of-fight hit lowers the bar by 360 with no player's hit behind it: no part of the fall that is shared out.
        var start = new List<StaggerEvent> { Bar(t, 1200), Bar(t.AddSeconds(1), 840), Bar(t.AddSeconds(2), 835) };
        StaggerShareResult scripted = StaggerShare.Compute(new[] { Hit(t.AddSeconds(2), 4, 100) }, start, boss, t.AddSeconds(-5), t.AddSeconds(10), id => drops.GetValueOrDefault(id));
        bool startHit = scripted.Total == 5 && scripted.Assigned == 5 && scripted.BySource.GetValueOrDefault(4) == 5;

        bool none = StaggerShare.Compute(hits, Array.Empty<StaggerEvent>(), boss, t, t, id => 5).Total == 0;
        bool ok = packet && totals && resets && none && startHit;
        Console.WriteLine($"  -> players credited in order={packet}, totals and unassigned={totals}, break resets={resets}, no readings={none}, scripted start hit left out={startHit}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
