using System.IO;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>Fight history: segmentation by silence gap, recording only finished fights, and a
/// SQLite round trip (insert, search, load with events intact, prune).</summary>
public static class SelfCheckHistory
{
    public static bool Run()
    {
        bool ok = true;
        ok &= RunSegmenterScenario();
        ok &= RunRecorderAndStoreScenario();
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

    private static bool RunRecorderAndStoreScenario()
    {
        Console.WriteLine("[selftest] Fight recorder + SQLite store:");
        string path = Path.Combine(Path.GetTempPath(), "aiondps-selftest-" + Guid.NewGuid().ToString("N"), "fights.db");
        try
        {
            using var store = new FightStore(path);
            var recorder = new FightRecorder(store);
            var start = new DateTime(2026, 9, 22, 20, 0, 0);
            List<DamageEvent> events = TwoRuns(start);
            var names = new Dictionary<int, string> { [You] = "You", [Healer] = "Sardine", [Boss] = "Raksha Boilheart", [Dummy] = "Training Dummy" };
            var context = new FightContext(
                NameOf: id => names.GetValueOrDefault(id, $"0x{id:X8}"),
                ClassOf: id => id == You ? "Gladiator" : id == Healer ? "Cleric" : "?",
                FactionOf: _ => "Elyos",
                IsPlayer: id => id is You or Healer,
                IsSelf: id => id == You,
                IsEnemy: _ => false,
                IsIgnoredTarget: name => name == "Training Dummy",
                Game: "aion",
                ServerName: "Origin Aion");

            // Mid-first-fight: nothing has ended yet.
            int duringFirst = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(30)).ToList(), start.AddSeconds(31), context);
            // Long after the first run, before the second: exactly the first run is filed.
            int afterFirst = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(58)).ToList(), start.AddMinutes(5), context);
            // Same call again must not file it twice.
            int again = recorder.Tick(events.Where(e => e.Timestamp <= start.AddSeconds(58)).ToList(), start.AddMinutes(6), context);
            // Flush at the end files the second run but never the dummy.
            int flushed = recorder.Tick(events, start.AddMinutes(21), context, flushAll: true);

            bool recordedWhenEnded = duringFirst == 0 && afterFirst == 1 && again == 0 && flushed == 1 && store.Count() == 2;

            List<FightSummary> all = store.Query(null);
            List<FightSummary> bySardine = store.Query("sardine");
            List<FightSummary> byBoss = store.Query("boilheart");
            bool searchWorks = all.Count == 2 && bySardine.Count == 1 && byBoss.Count == 2 && all[0].StartedAt > all[1].StartedAt;

            FightDetail? first = store.Load(bySardine[0].Id);
            bool detailIntact = first is not null
                && first.Summary.TargetName == "Raksha Boilheart"
                && first.Summary.SelfName == "You"
                && first.Participants.Count == 2
                && first.Participants[0].Name == "You" && first.Participants[0].Damage == Enumerable.Range(0, 30).Sum(i => 1000 + i)
                && first.Participants[0].DamageTaken == 500
                && first.Participants.Single(p => p.Name == "Sardine").Healing == 400
                && first.Events.Count == 32
                && first.Events.All(e => e.Timestamp.Kind == DateTimeKind.Unspecified)
                && first.Events.Any(e => e.Skill == "Cleave")
                && first.Names[Boss] == "Raksha Boilheart";

            int pruned = store.Prune(retentionDays: 3650, maxFights: 1);
            bool pruneKeepsNewest = pruned == 1 && store.Count() == 1 && store.Query(null)[0].StartedAt == start.AddMinutes(10);

            Console.WriteLine($"  -> fights are filed once, only after the silence gap or a flush, never the dummy: {recordedWhenEnded}");
            Console.WriteLine($"  -> search by target or participant, newest first: {searchWorks}");
            Console.WriteLine($"  -> loaded detail keeps participants, events, skills and names: {detailIntact}");
            Console.WriteLine($"  -> prune to a cap keeps the newest fight: {pruneKeepsNewest}");
            return recordedWhenEnded && searchWorks && detailIntact && pruneKeepsNewest;
        }
        finally
        {
            try
            {
                Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
