using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>Fight segmentation by silence gap (used by the session analysis).</summary>
public static class SelfCheckHistory
{
    public static bool Run()
    {
        bool ok = true;
        ok &= RunSegmenterScenario();
        return ok;
    }

    private const int You = 1;
    private const int Healer = 2;
    private const int Boss = 100;
    private const int Dummy = 101;

    private static List<DamageEvent> TwoRuns(DateTime start)
    {
        var events = new List<DamageEvent>();
        for (int i = 0; i < 30; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i * 2), You, Boss, 1000 + i, IsHeal: false, Skill: "Cleave"));
        }
        events.Add(new DamageEvent(start.AddSeconds(20), Boss, You, 500, IsHeal: false));
        events.Add(new DamageEvent(start.AddSeconds(21), Healer, You, 400, IsHeal: true, Skill: "Healing Light"));
        // Ten minutes later: the same boss again - a separate run.
        DateTime second = start.AddMinutes(10);
        for (int i = 0; i < 20; i++)
        {
            events.Add(new DamageEvent(second.AddSeconds(i * 3), You, Boss, 2000, IsHeal: false));
        }
        // A training dummy poke that must never be recorded.
        events.Add(new DamageEvent(start.AddMinutes(20), You, Dummy, 1, IsHeal: false));
        events.Add(new DamageEvent(start.AddMinutes(20).AddSeconds(30), You, Dummy, 1, IsHeal: false));
        return events;
    }

    private static bool RunSegmenterScenario()
    {
        Console.WriteLine("[selftest] Fight segmentation (120 s silence gap):");
        var start = new DateTime(2026, 9, 22, 20, 0, 0);
        List<FightSegment> segments = FightSegmenter.Segment(TwoRuns(start), Boss);
        bool twoRuns = segments.Count == 2;
        bool firstBounds = twoRuns && segments[0].Start == start && segments[0].End == start.AddSeconds(58) && segments[0].Hits.Count == 30;
        bool secondBounds = twoRuns && segments[1].Start == start.AddMinutes(10) && segments[1].Hits.Count == 20;
        bool healsExcluded = twoRuns && segments[0].Hits.All(h => !h.IsHeal && h.TargetObjectId == Boss);
        Console.WriteLine($"  -> two runs, split at the ten-minute gap: {twoRuns}");
        Console.WriteLine($"  -> first run spans its own hits only: {firstBounds}");
        Console.WriteLine($"  -> second run picked up whole: {secondBounds}");
        Console.WriteLine($"  -> heals and the boss's own hits are not part of a run: {healsExcluded}");
        return twoRuns && firstBounds && secondBounds && healsExcluded;
    }
}
