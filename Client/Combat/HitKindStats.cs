namespace AionDPS.Combat;

/// <summary>How much more (or less) a hit did when it had a flag, against the same skill without it - the check that a flag read from the hit data means what it is named.</summary>
public sealed record HitKindEffect(string Name, int Hits, int SkillsCompared, double MedianRatio);

public static class HitKindStats
{
    private const int MinPerGroup = 8;

    /// <summary>For each flag (and crit and multi-hit): over the skills with at least eight hits with and without it, the median of the ratio of the median damage.</summary>
    public static IReadOnlyList<HitKindEffect> Effects(IReadOnlyList<DamageEvent> events)
    {
        var hits = events.Where(e => !e.IsHeal && !e.IsTick && e.Kinds is not null && e.Amount > 0).ToList();
        var tests = new (string Name, Func<DamageEvent, bool> Has)[]
        {
            ("Crit", e => e.IsCritical),
            ("Double", e => (e.Kinds!.Value & HitKinds.Double) != 0),
            ("Perfect", e => (e.Kinds!.Value & HitKinds.Perfect) != 0),
            ("Parry", e => (e.Kinds!.Value & HitKinds.Parry) != 0),
            ("Back", e => (e.Kinds!.Value & HitKinds.Back) != 0),
            ("Front", e => (e.Kinds!.Value & HitKinds.Front) != 0),
            ("Multi", e => e.HitCount > 1),
        };
        var result = new List<HitKindEffect>();
        foreach (var (name, has) in tests)
        {
            var ratios = new List<double>();
            foreach (var skill in hits.GroupBy(e => e.Skill ?? ""))
            {
                var yes = skill.Where(has).Select(e => (double)e.Amount).OrderBy(v => v).ToList();
                var no = skill.Where(e => !has(e)).Select(e => (double)e.Amount).OrderBy(v => v).ToList();
                if (yes.Count >= MinPerGroup && no.Count >= MinPerGroup)
                {
                    ratios.Add(Median(yes) / Median(no));
                }
            }

            result.Add(new HitKindEffect(name, hits.Count(has), ratios.Count, ratios.Count > 0 ? Median(ratios.OrderBy(r => r).ToList()) : 0));
        }

        return result;
    }

    private static double Median(List<double> sorted) => sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
