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
            // Welch's test by hand: A = 10..14, B = 12..16 (both SD 1.581, n 5): difference 2, error 1, t 2, df 8, p 0.0805, 95 % range 2 +/- 2.306 (not significant); B = 14..18: t 4, range 1.69 .. 6.31 (significant)
            WelchResult? w1 = DummyStatistics.Welch(new double[] { 10, 11, 12, 13, 14 }, new double[] { 12, 13, 14, 15, 16 });
            WelchResult? w2 = DummyStatistics.Welch(new double[] { 10, 11, 12, 13, 14 }, new double[] { 14, 15, 16, 17, 18 });
            bool welch = w1 is { } a1 && Math.Abs(a1.Difference - 2) < 1e-9 && Math.Abs(a1.StandardError - 1) < 1e-9 && Math.Abs(a1.Df - 8) < 1e-9 && a1.P is > 0.080 and < 0.081
                && Math.Abs(a1.Low - (2 - 2.306)) < 0.002 && !a1.Significant && w2 is { } a2 && Math.Abs(a2.T - 4) < 1e-9 && a2.Significant && Math.Abs(a2.Low - 1.694) < 0.002
                && DummyStatistics.Welch(new double[] { 1 }, new double[] { 1, 2 }) is null && Math.Abs(DummyStatistics.Median(new double[] { 5, 1, 3, 2 }) - 2.5) < 1e-9;
            Console.WriteLine($"  -> Welch's test against hand-computed values (difference, error, df, p, the 95 % range), a side with one value gives none: {welch}");

            // two runs: skill X hits 1,000 +/- 20 in A and 1,100 +/- 20 in B (better), skill Y alike in both (no difference), skill Z only 5 hits (not enough); one in five hits of X is a crit of twice the damage, both sides
            DummyRun Make(double xBase, int zHits, int seed)
            {
                var dice = new Random(seed);
                var run = new DummyRun { Start = t0, Planned = TimeSpan.FromSeconds(60), Length = TimeSpan.FromSeconds(60) };
                for (int i = 0; i < 60; i++)
                {
                    bool crit = i % 5 == 0;
                    run.Events.Add(new DamageEvent(t0.AddSeconds(i * 0.9), you, dummy, (long)(xBase * (0.98 + 0.04 * dice.NextDouble()) * (crit ? 2 : 1)), false, "X", IsCritical: crit, SkillId: 11010340));
                    run.Events.Add(new DamageEvent(t0.AddSeconds(i * 0.9 + 0.3), you, dummy, (long)(500 * (0.98 + 0.04 * dice.NextDouble())), false, "Y", SkillId: 11420340));
                }

                for (int i = 0; i < zHits; i++)
                {
                    run.Events.Add(new DamageEvent(t0.AddSeconds(i * 5 + 0.6), you, dummy, 2000, false, "Z", SkillId: 12030340));
                }

                return run;
            }

            DummyRun refRun = Make(1000, 5, 1), betterRun = Make(1100, 5, 2), sameRun = Make(1000, 5, 3);
            var multipliers = DummyComparison.EstimateMultipliers(refRun.Events.Concat(betterRun.Events).Concat(sameRun.Events));
            DummyRunComparison versusBetter = DummyComparison.Compare(refRun, betterRun, multipliers);
            DummyRunComparison versusSame = DummyComparison.Compare(refRun, sameRun, multipliers);
            DummySkillResult? x = versusBetter.Skills.FirstOrDefault(r => r.Name == "X");
            DummySkillResult? y = versusBetter.Skills.FirstOrDefault(r => r.Name == "Y");
            bool skills = x is { Verdict: DummyVerdict.Better } && x.ChangePercent is > 9 and < 11 && x.HitsA == 60 // the 12 crits are brought back to plain hits, so all 60 count
                && y is { Verdict: DummyVerdict.NoDifference } && versusBetter.NotMeasurable == 1 // Z: 5 hits
                && versusBetter.Skills.All(r => r.Name != "Z") && multipliers.Values.Any(m => Math.Abs(m - 2) < 0.1);
            // +10 % on one skill's hit is within the noise of the whole run (the crits alone spread a run's damage that much); +60 % is far above it; runs alike are within it
            DummyRun muchBetter = Make(1600, 5, 4);
            DummyRunComparison versusMuch = DummyComparison.Compare(refRun, muchBetter, DummyComparison.EstimateMultipliers(refRun.Events.Concat(muchBetter.Events)));
            bool noise = versusMuch.Noise is { AboveNoise: true } && versusBetter.Noise is { AboveNoise: false } && versusSame.Noise is { AboveNoise: false } && versusSame.Skills.All(r => r.Verdict == DummyVerdict.NoDifference);
            Console.WriteLine($"  -> two runs: a skill hit harder is better (+10 %), one alike has no measurable difference, one with 5 hits is not measurable, the crits are brought back to plain: {skills}");
            Console.WriteLine($"  -> the iDPS change: +60 % is above the noise, +10 % of one skill and runs alike are within it: {noise}");
            // the skill entries the game sends when a level changes: 0x1338 (Identify Weakness 11740000, level 18 = base 10 + 8), 0x0F38 (variant 11010350, level 17), 0x1038 takes a variant away
            var skillDirectory = new Aion2.Aion2EntityDirectory();
            var skillDecoder = new Aion2.Protocol.Aion2FrameDecoder(Aion2.Protocol.Aion2Protocol.Load(), skillDirectory);
            bool beforeLevels = !skillDirectory.SkillLevelsLive;
            skillDecoder.Decode(Convert.FromHexString("1338" + "01" + "6023b300" + "01" + "6023b300" + "120a040002020 00a".Replace(" ", "")), t0);
            skillDecoder.Decode(Convert.FromHexString("0f38" + "01" + "2e01a800" + "110a0400010200" + "06"), t0);
            bool entries = skillDirectory.SkillLevelsLive && skillDirectory.LocalSkills.Any(k => k.SkillId == 11740000 && k.Level == 18 && k.BaseLevel == 10)
                && skillDirectory.LocalSkills.Any(k => k.SkillId == 11010350 && k.Level == 17);
            skillDecoder.Decode(Convert.FromHexString("1038" + "2e01a800"), t0);
            skillDecoder.Decode(Convert.FromHexString("1338" + "01" + "6023b300" + "01" + "6023b300" + "140a040002020 00a".Replace(" ", "")), t0); // the level goes to 20
            bool removed = skillDirectory.LocalSkills.All(k => k.SkillId != 11010350) && skillDirectory.LocalSkills.Single(k => k.SkillId == 11740000).Level == 20;
            Console.WriteLine($"  -> skill entries (0x1338, 0x0F38) set a level at once, 0x1038 takes a variant away, a later entry changes the level: {beforeLevels && entries && removed}");

            // the runs keep hit kinds, skill levels and a file of before (no kinds, no levels) still loads
            string levelPath = Path.Combine(Path.GetTempPath(), "dummy-levels-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var kept = new DummyRun { Number = 1, Start = t0, Planned = TimeSpan.FromSeconds(60), Length = TimeSpan.FromSeconds(60), Closed = true, SkillLevels = new Dictionary<int, int> { [11010000] = 18 } };
                kept.Events.Add(new DamageEvent(t0, you, dummy, 700, false, "X", Kinds: HitKinds.Double | HitKinds.Perfect, SkillId: 11010340));
                DummyStore.Save(levelPath, new[] { kept });
                DummyRun loaded = DummyStore.Load(levelPath).Single();
                File.WriteAllText(levelPath, "[{\"Number\":1,\"Start\":638000000000000000,\"PlannedSeconds\":60,\"LengthSeconds\":60,\"Note\":\"\",\"PlayerId\":1,\"Events\":[{\"T\":638000000000000000,\"S\":1,\"G\":100,\"A\":500,\"Sk\":\"X\",\"C\":false,\"Tk\":false,\"Id\":0,\"N\":1}],\"Uses\":[],\"Buffs\":[],\"Names\":{}}]");
                DummyRun old = DummyStore.Load(levelPath).Single();
                bool store = loaded.Events[0].Kinds == (HitKinds.Double | HitKinds.Perfect) && loaded.SkillLevels is { } lv && lv[11010000] == 18 && old.Events.Count == 1 && old.Events[0].Kinds is null && old.SkillLevels is null;
                Console.WriteLine($"  -> a saved run keeps its hit kinds and skill levels, a file from before them still loads: {store}");
                ok &= store;
            }
            finally
            {
                File.Delete(levelPath);
            }

            // levels in the comparison: one skill differs (X 18 -> 20), and three runs at X 18, 20 and 22 hitting 1,000, 1,100 and 1,200 give +5 % a level
            DummyRun atLevel(DummyRun r, int levelX)
            {
                r.SkillLevels = new Dictionary<int, int> { [11010000] = levelX, [11420000] = 10 };
                return r;
            }

            DummyRun l18 = atLevel(Make(1000, 5, 11), 18), l20 = atLevel(Make(1100, 5, 12), 20), l22 = atLevel(Make(1200, 5, 13), 22);
            var levelMultipliers = DummyComparison.EstimateMultipliers(l18.Events.Concat(l20.Events).Concat(l22.Events));
            DummyRunComparison levelCompare = DummyComparison.Compare(l18, l20, levelMultipliers);
            var curve = DummyComparison.LevelCurves(new[] { l18, l20, l22 }, levelMultipliers).FirstOrDefault(c => c.Name == "X");
            bool levels = levelCompare.LevelsKnown && levelCompare.DifferingLevels is { Count: 1 } one && one[0] == "X" && levelCompare.Skills.First(r => r.Name == "X") is { LevelA: 18, LevelB: 20 }
                && DummyComparison.Compare(l18, l18, levelMultipliers).DifferingLevels is { Count: 0 } && !DummyComparison.Compare(refRun, betterRun, multipliers).LevelsKnown
                && curve is { LowLevel: 18, HighLevel: 22, Runs: 3 } && Math.Abs(curve.PercentPerLevel - 5) < 0.6 && curve.IdpsPerLevel is > 0
                && DummyComparison.LevelCurves(new[] { l18, atLevel(Make(1100, 5, 14), 18) }, levelMultipliers).Count == 0; // one level only: no curve
            Console.WriteLine($"  -> levels: the differing skill is named, none differ for equal runs, unknown levels say so, 18/20/22 give +5 % a level, one level gives no curve: {levels}");
            return ok && parse && clock && chains && rates && steadyRates && targets && welch && skills && noise && levels;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
