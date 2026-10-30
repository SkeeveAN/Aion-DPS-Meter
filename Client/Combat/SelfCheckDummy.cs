using System.IO;
using AionDPS.History;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The dummy test: the clock waits for the first own hit or skill, counts the planned time and keeps only the player's own events inside it; runs survive their file.</summary>
public static class SelfCheckDummy
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Dummy test:");
        const int you = 1, other = 2, dummy = 100;
        DateTime t0 = new(2026, 10, 13, 21, 0, 0);
        int skill = 17278949, buff = 17000010;
        var test = new DummyTest();
        int captured = 0;
        test.Capture = run =>
        {
            captured++;
            run.Uses.Add(new SkillUse(run.Start.AddSeconds(1), you, skill));
            run.Buffs.Add(new BuffEvent(run.Start, BuffKind.On, you, 1, buff, you, 10_000));
        };

        test.Arm(TimeSpan.FromSeconds(60), "Rota A", t0);
        // before the first hit: nothing runs; someone else's hit and a hit from before the start do not start it either
        test.Feed(new[] { new DamageEvent(t0.AddSeconds(-1), you, dummy, 5, false), new DamageEvent(t0.AddSeconds(1), other, dummy, 5, false) }, id => id == you);
        bool waits = test.State == DummyState.Armed && test.Current is null && test.Remaining(t0.AddSeconds(2)) == TimeSpan.FromSeconds(60);

        var hits = new List<DamageEvent>();
        for (int i = 0; i < 70; i++)
        {
            hits.Add(new DamageEvent(t0.AddSeconds(10 + i), you, dummy, 1000, false, "Strike", IsCritical: i % 10 == 0));
        }

        hits.Add(new DamageEvent(t0.AddSeconds(30), other, dummy, 999, false));
        test.Feed(hits, id => id == you);
        DummyRun? current = test.Current;
        bool started = test.State == DummyState.Running && current is not null && current.Start == t0.AddSeconds(10);
        bool counted = current is not null && current.Events.Count == 61 && current.Events.All(e => e.SourceObjectId == you); // 10 s .. 70 s
        bool countdown = test.Remaining(t0.AddSeconds(40)) == TimeSpan.FromSeconds(30);
        Console.WriteLine($"  -> waits for the first own hit, the clock starts with it, the countdown counts the planned time: {waits && started && countdown}");
        Console.WriteLine($"  -> only own hits inside the time are kept (61 of 70): {counted}");

        test.Tick(t0.AddSeconds(70.1));
        bool over = test.State == DummyState.Finished && current is { Closed: false };
        test.Tick(t0.AddSeconds(70 + 3));
        bool closed = current is { Closed: true } && captured == 1 && current.Uses.Count == 1;
        test.Tick(t0.AddSeconds(70 + 25));
        bool idle = test.State == DummyState.Idle;
        Console.WriteLine($"  -> time up: finished at once, skills and effects read a moment later, the overlay clears after a while: {over && closed && idle}");

        DummyStats stats = DummyAnalysis.Summarize(current!);
        bool numbers = stats.Total == 61_000 && stats.Hits == 61 && Math.Abs(stats.AvgDps - 61_000 / 60.0) < 0.01 && stats.MaxDps == 2000 && stats.MinDps == 1000
            && stats.MinHit == 1000 && Math.Abs(stats.CritPercent - 100.0 * 7 / 61) < 0.01;
        Console.WriteLine($"  -> the run's numbers (total, hits, average, slowest and fastest second, crit): {numbers}");

        // a second Start makes a second run, and a cast before the first hit moves the start back to it
        test.Arm(TimeSpan.FromSeconds(30), "", t0.AddMinutes(5));
        test.Feed(new[] { new DamageEvent(t0.AddMinutes(5).AddSeconds(2), you, dummy, 10, false) }, id => id == you);
        test.NoteActivity(t0.AddMinutes(5).AddSeconds(1.2), you);
        test.Stop(t0.AddMinutes(5).AddSeconds(10));
        DummyRun second = test.Runs[^1];
        bool again = test.Runs.Count == 2 && second.Number == 2 && second.Start == t0.AddMinutes(5).AddSeconds(1.2) && Math.Abs((second.Length - TimeSpan.FromSeconds(8.8)).TotalMilliseconds) < 1;
        Console.WriteLine($"  -> every Start is its own run; a cast before the first hit moves the start back; Stop keeps the time until then: {again}");

        // an armed test that is never started can be taken back; an effect of someone else does not start it
        var other2 = new DummyTest();
        other2.Arm(TimeSpan.FromSeconds(30), "", t0);
        other2.Cancel();
        bool cancelled = other2.State == DummyState.Idle && other2.Runs.Count == 0;

        // the file
        string path = Path.Combine(Path.GetTempPath(), "dummy-selftest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            current!.Note = "Rota A";
            DummyStore.Save(path, test.Runs.Where(r => r.Closed));
            List<DummyRun> back = DummyStore.Load(path);
            bool roundTrip = back.Count == 1 && back[0].Note == "Rota A" && back[0].Events.Count == 61 && back[0].Uses.Count == 1 && back[0].Buffs.Count == 1
                && back[0].Length == TimeSpan.FromSeconds(60) && back[0].Start == current.Start;
            var lanes = DummyAnalysis.Timeline(new[] { back[0] });
            bool timeline = lanes.Count(l => !l.IsEffect) == 1 && lanes.Count(l => l.IsEffect) == 1 && lanes.First(l => l.IsEffect).PerRun[0].Count == 1;
            Console.WriteLine($"  -> runs survive their file, the timeline has one skill line and one effect line: {roundTrip && timeline && cancelled}");
            bool ok = waits && started && countdown && counted && over && closed && idle && numbers && again && cancelled && roundTrip && timeline;

            bool parse = DummyTestWindow.TryParseDuration("2:00", out TimeSpan two) && two == TimeSpan.FromMinutes(2)
                && DummyTestWindow.TryParseDuration("90", out TimeSpan ninety) && ninety == TimeSpan.FromSeconds(90)
                && !DummyTestWindow.TryParseDuration("abc", out _) && !DummyTestWindow.TryParseDuration("0:02", out _) && !DummyTestWindow.TryParseDuration("2:61:00:1", out _);
            bool clock = DummyTestController.Clock(TimeSpan.FromMinutes(2)) == "2:00:00" && DummyTestController.Clock(TimeSpan.FromMilliseconds(61_340)) == "1:01:34";
            Console.WriteLine($"  -> the time field reads 2:00 and 90, the overlay writes 2:00:00: {parse && clock}");
            // the chain skills: Rending Blow (11010340) -> Smashing Blow (11420340) within 3 s once, once too late, once never
            var chained = new DummyRun { Start = t0, Length = TimeSpan.FromSeconds(60) };
            chained.Uses.Add(new SkillUse(t0.AddSeconds(1), you, 11010340));
            chained.Uses.Add(new SkillUse(t0.AddSeconds(2), you, 11420340));
            chained.Uses.Add(new SkillUse(t0.AddSeconds(10), you, 11010340));
            chained.Uses.Add(new SkillUse(t0.AddSeconds(14), you, 11420340));
            chained.Uses.Add(new SkillUse(t0.AddSeconds(20), you, 11010340));
            bool chains = DummyAnalysis.Chains(chained) == (3, 1);
            Console.WriteLine($"  -> the chain skills taken (1 of 3) of a run: {chains}");
            // DPS leaves every pause out, iDPS counts the whole span: a caster uses three strong skills at 0-3 s and again at 30-33 s, 600,000 damage each time
            var burst = new DummyRun { Start = t0, Planned = TimeSpan.FromSeconds(60), Length = TimeSpan.FromSeconds(60) };
            foreach (double at in new[] { 0, 1.2, 2.5, 30, 31.2, 32.5 })
            {
                burst.Events.Add(new DamageEvent(t0.AddSeconds(at), you, dummy, 200_000, false));
            }

            double? burstDps = DummyAnalysis.ActiveDps(burst);
            double? burstIdps = DummyAnalysis.IDps(burst);
            // 1,200,000 over 6 slices of 1 s with damage = 200,000; over 32.5 s from the first to the last hit = 36,923
            bool rates = burstDps is { } bd && Math.Abs(bd - 200_000) < 1 && burstIdps is { } bi && Math.Abs(bi - 1_200_000 / 32.5) < 1 && burstDps > burstIdps * 5
                && DummyAnalysis.ActiveDps(new DummyRun { Start = t0 }) is null && DummyAnalysis.IDps(new DummyRun { Start = t0 }) is null;
            // a steady attack (one hit every second) has the same figure both ways, up to the one slice the span does not count
            var steady = new DummyRun { Start = t0, Length = TimeSpan.FromSeconds(60) };
            for (int i = 0; i < 60; i++)
            {
                steady.Events.Add(new DamageEvent(t0.AddSeconds(i), you, dummy, 1000, false));
            }

            bool steadyRates = DummyAnalysis.ActiveDps(steady) is { } sd && Math.Abs(sd - 1000) < 1 && DummyAnalysis.IDps(steady) is { } si && Math.Abs(si - 60_000.0 / 59) < 1;
            Console.WriteLine($"  -> DPS leaves the pauses out (200,000), iDPS counts the whole span (36,923); a steady attack gives both about alike: {rates && steadyRates}");

            // the target: the name a run kept decides; a dummy in any language, a boss is none, a run without the name is unknown
            var atDummy = new DummyRun { Start = t0 };
            atDummy.Events.Add(new DamageEvent(t0, you, dummy, 5, false));
            atDummy.Names[dummy] = "Sandsack";
            var atBoss = new DummyRun { Start = t0 };
            atBoss.Events.Add(new DamageEvent(t0, you, 101, 5, false));
            atBoss.Names[101] = "Gadioton";
            var noName = new DummyRun { Start = t0 };
            noName.Events.Add(new DamageEvent(t0, you, 102, 5, false));
            bool targets = DummyAnalysis.AtDummy(atDummy) == true && DummyAnalysis.AtDummy(atBoss) == false && DummyAnalysis.AtDummy(noName) is null
                && DummyTargets.IsDummy("Punching Bag_Elyos Tribe") && DummyTargets.IsDummy("Nahkampf-Trainingspuppe") && !DummyTargets.IsDummy("Vergessener Gadioton");
            Console.WriteLine($"  -> the target of a run is a dummy / not / unknown by the name it kept: {targets}");
            return ok && parse && clock && chains && rates && steadyRates && targets;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
