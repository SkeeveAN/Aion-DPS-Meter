using System.Globalization;
using AionDPS.History;

namespace AionDPS.Combat;

public enum DummyVerdict { Better, Worse, NoDifference, NotEnoughData }

/// <summary>One skill of two runs: the mean of a plain hit on each side (the other kinds of hit brought back to a plain one) and what the difference is worth with its 95 % interval.</summary>
public sealed record DummySkillResult(string Name, int HitsA, int HitsB, double MeanA, double MeanB, double? ChangePercent, double? LowPercent, double? HighPercent, DummyVerdict Verdict, long TotalA);

/// <summary>The iDPS of the two runs and whether the difference is larger than the noise: the spread the hits themselves have (their random damage, crit, double and perfect hits).</summary>
public sealed record DummyNoise(double IdpsA, double IdpsB, double Change, double ChangePercent, double StandardError, bool AboveNoise);

public sealed record DummyRunComparison(DummyRun Reference, DummyRun Other, DummyNoise? Noise, IReadOnlyList<DummySkillResult> Skills, int NotMeasurable);

/// <summary>
/// The dummy test's statistics: is the difference between two runs real, or only the luck of the hits? Two answers. The iDPS of a run is compared with the noise its own hits make
/// (the hits' variance adds up to the damage's variance; the rotation is the player's, so only the dice are counted). A skill's hit is compared apart from the rotation: its plain hits,
/// and the crit, double and perfect hits divided by the multiplier measured for that skill, are two samples tested with Welch's t-test; no verdict below 10 hits a side.
/// The approach, the 10-hit limits and the per-skill multiplier are Stacy's, from her fork (StacysView/Aion-DPS-Meter, MIT, 23 October 2026); this is its short form for one run against one run.
/// Pure: no windows, no packets.
/// </summary>
public static class DummyComparison
{
    /// <summary>Fewer usable hits than this on a side and a skill's hit is not compared.</summary>
    public const int MinHits = 10;

    /// <summary>A multiplier for crit, double or perfect hits needs this many plain hits and this many of the kind, the runs together; with fewer its own error would show as a difference.</summary>
    public const int MinCorrectionHits = 10;

    private static string KeyOf(DamageEvent e) => e.SkillId != 0 ? "#" + DummyAnalysis.BaseOf(e.SkillId).ToString(CultureInfo.InvariantCulture) : "name:" + (e.Skill ?? "(auto attack)");

    private static bool IsPlain(DamageEvent e) => !e.IsTick && !e.IsCritical && (e.Kinds ?? HitKinds.None) == HitKinds.None;

    /// <summary>The kind of a hit as one number: 0 for a plain hit, else crit in bit 0 and the flags (parry, perfect, double, back, front) shifted up by one.</summary>
    private static int ClassOf(DamageEvent e) => (e.IsCritical ? 1 : 0) | ((int)(e.Kinds ?? HitKinds.None) << 1);

    private static double PerStrike(DamageEvent e) => (double)e.Amount / Math.Max(1, e.HitCount);

    private static IEnumerable<DamageEvent> DamageOf(DummyRun run) => run.Events.Where(e => !e.IsHeal);

    /// <summary>
    /// How much more a crit (or perfect, double ...) hit does than a plain one, per skill, measured on the events of the runs compared: the median of the kind over the median of the plain
    /// hits, with at least <see cref="MinCorrectionHits"/> of each. Per skill, because a crit is not worth the same in every skill. Both sides share the multipliers, so neither is favoured.
    /// </summary>
    public static IReadOnlyDictionary<(string Key, int Class), double> EstimateMultipliers(IEnumerable<DamageEvent> events)
    {
        var result = new Dictionary<(string, int), double>();
        foreach (IGrouping<string, DamageEvent> skill in events.Where(e => !e.IsTick && !e.IsHeal).GroupBy(KeyOf))
        {
            List<double> plain = skill.Where(IsPlain).Select(PerStrike).ToList();
            if (plain.Count < MinCorrectionHits)
            {
                continue;
            }

            double plainMedian = DummyStatistics.Median(plain);
            if (plainMedian <= 0)
            {
                continue;
            }

            foreach (IGrouping<int, DamageEvent> kind in skill.Where(e => !IsPlain(e)).GroupBy(ClassOf))
            {
                List<double> amounts = kind.Select(PerStrike).ToList();
                if (amounts.Count >= MinCorrectionHits && DummyStatistics.Median(amounts) / plainMedian is > 0.2 and < 8 and var multiplier)
                {
                    result[(skill.Key, kind.Key)] = multiplier;
                }
            }
        }

        return result;
    }

    /// <summary>The hits of a skill as plain ones: the plain hits as they are, the other kinds divided by their multiplier (left out when none could be measured).</summary>
    private static List<double> Corrected(IEnumerable<DamageEvent> hits, string key, IReadOnlyDictionary<(string Key, int Class), double> multipliers)
    {
        var sample = new List<double>();
        foreach (DamageEvent e in hits.Where(e => !e.IsTick))
        {
            if (IsPlain(e))
            {
                sample.Add(PerStrike(e));
            }
            else if (multipliers.TryGetValue((key, ClassOf(e)), out double m) && m > 0)
            {
                sample.Add(PerStrike(e) / m);
            }
        }

        return sample;
    }

    /// <summary>The standard deviation of a run's iDPS caused by its hits alone: the variance of its damage is the hits' variances added, divided by the span squared.</summary>
    private static double IdpsError(DummyRun run, double span)
    {
        double variance = 0;
        foreach (IGrouping<string, DamageEvent> skill in DamageOf(run).GroupBy(KeyOf))
        {
            foreach (IEnumerable<DamageEvent> part in new[] { skill.Where(e => !e.IsTick), skill.Where(e => e.IsTick) })
            {
                List<double> amounts = part.Select(e => (double)e.Amount).ToList();
                variance += amounts.Count * DummyStatistics.SampleVariance(amounts);
            }
        }

        return span > 0 ? Math.Sqrt(variance) / span : 0;
    }

    private static double Span(DummyRun run)
    {
        List<DamageEvent> events = DamageOf(run).ToList();
        return events.Count > 1 ? (events.Max(e => e.Timestamp) - events.Min(e => e.Timestamp)).TotalSeconds : 0;
    }

    /// <summary>The other run against the reference. <paramref name="multipliers"/> are measured on the events of all the runs being compared (see <see cref="EstimateMultipliers"/>).</summary>
    public static DummyRunComparison Compare(DummyRun reference, DummyRun other, IReadOnlyDictionary<(string Key, int Class), double> multipliers)
    {
        DummyNoise? noise = null;
        if (DummyAnalysis.IDps(reference) is { } idpsA && DummyAnalysis.IDps(other) is { } idpsB && idpsA > 0)
        {
            double seA = IdpsError(reference, Span(reference));
            double seB = IdpsError(other, Span(other));
            double se = Math.Sqrt(seA * seA + seB * seB);
            double change = idpsB - idpsA;
            noise = new DummyNoise(idpsA, idpsB, change, 100 * change / idpsA, se, Math.Abs(change) > DummyStatistics.Z95 * se);
        }

        var results = new List<DummySkillResult>();
        int notMeasurable = 0;
        var byKeyA = DamageOf(reference).GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList());
        var byKeyB = DamageOf(other).GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList());
        foreach (string key in byKeyA.Keys.Union(byKeyB.Keys))
        {
            List<DamageEvent> hitsA = byKeyA.GetValueOrDefault(key) ?? new();
            List<DamageEvent> hitsB = byKeyB.GetValueOrDefault(key) ?? new();
            string name = hitsA.Concat(hitsB).Select(e => e.Skill).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? key;
            List<double> a = Corrected(hitsA, key, multipliers);
            List<double> b = Corrected(hitsB, key, multipliers);
            long total = hitsA.Sum(e => e.Amount);
            if (a.Count < MinHits || b.Count < MinHits)
            {
                notMeasurable++;
                continue;
            }

            WelchResult? welch = DummyStatistics.Welch(a, b);
            double meanA = DummyStatistics.Mean(a);
            if (welch is not { } w || meanA <= 0)
            {
                notMeasurable++;
                continue;
            }

            DummyVerdict verdict = w.Significant ? (w.Difference > 0 ? DummyVerdict.Better : DummyVerdict.Worse) : DummyVerdict.NoDifference;
            results.Add(new DummySkillResult(name, a.Count, b.Count, meanA, DummyStatistics.Mean(b), 100 * w.Difference / meanA, 100 * w.Low / meanA, 100 * w.High / meanA, verdict, total));
        }

        return new DummyRunComparison(reference, other, noise, results.OrderByDescending(r => r.TotalA).ToList(), notMeasurable);
    }
}
