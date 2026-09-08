namespace AionSniffer.Combat;

/// <summary>
/// Works out which hits were critical from the damage numbers alone, for players whose client
/// never told us.
///
/// <para><b>Why this is needed at all.</b> Aion only marks critical hits reliably in the log of the
/// player who scored them. Measured across four logs of one Sauro Supply Base run: the same 2374
/// hits by one Assassin were marked as 19,0% crits in his own client and 8,8% in both other
/// players' -- less than half. A third client marked none at all, because its chat filter for crit
/// notifications was off. Reading the flag for anyone but the local player therefore reports a
/// floor, not a rate.</para>
///
/// <para><b>How.</b> A crit does about 2,3x a normal hit -- measured per skill on labelled data:
/// 2,28x, 2,32x, 2,40x, 2,50x across four skills. So within one skill against one target the
/// damage values fall into two clusters, and a threshold between them separates crits from normal
/// hits. Grouping by target as well as skill matters: bosses differ in defence, so the same skill
/// lands for different amounts on different ones, and pooling them smears the two clusters
/// together.</para>
///
/// <para><b>How well.</b> Validated against an Assassin's own German log, where every crit is
/// flagged, over 2118 hits: 95,8% of hits classified correctly, 379 of 387 real crits found
/// (97,9%), 81 false positives. It therefore overstates by about 3,4 percentage points -- the
/// false positives are probably buffed hits or hits on a weakened target. Applied to a third
/// party's view of that same Assassin it returned 21,7%, or 18,3% after that correction, against
/// the 19,0% his own client recorded.</para>
/// </summary>
public static class CritEstimator
{
    /// <summary>
    /// Multiple of the lower cluster's median above which a hit is taken to be a crit. Halfway
    /// between a normal hit and the ~2,3x a crit lands for. Deliberately not tuned finer: accuracy
    /// moves by less than half a point anywhere between 1,5 and 2,0, so a sharper value would be
    /// fitting noise from one log.
    /// </summary>
    private const double CritThresholdFactor = 1.7;

    /// <summary>
    /// Fewer hits than this in a skill/target group and no split is attempted -- everything counts
    /// as a normal hit. A handful of samples cannot show two clusters, and guessing from three
    /// values would produce a crit rate of 0% or 33% at random.
    /// </summary>
    private const int MinimumSampleSize = 6;

    /// <summary>
    /// The systematic overstatement measured against labelled data, in percentage points. Callers
    /// showing a rate to a human should subtract it; callers marking individual hits should not,
    /// since there is no such thing as a fractional hit.
    /// </summary>
    public const double OverstatementPercentagePoints = 3.4;

    /// <summary>
    /// Marks each event as critical or not. <paramref name="trustLoggedFlag"/> is for the local
    /// player, whose client does flag its own crits properly: there the log beats any estimate, and
    /// guessing over a known answer would only add error.
    /// </summary>
    public static Dictionary<DamageEvent, bool> Estimate(IReadOnlyList<DamageEvent> events, bool trustLoggedFlag)
    {
        var result = new Dictionary<DamageEvent, bool>();
        if (trustLoggedFlag)
        {
            foreach (DamageEvent e in events)
            {
                result[e] = e.IsCritical;
            }

            return result;
        }

        foreach (IGrouping<(string, int), DamageEvent> group in events
                     .Where(e => !e.IsHeal)
                     .GroupBy(e => (e.Skill ?? "", e.TargetObjectId)))
        {
            var hits = group.ToList();
            double threshold = ThresholdFor(hits.Select(h => h.Amount).ToList());
            foreach (DamageEvent e in hits)
            {
                // A logged crit stays a crit even when the estimate would disagree: the flag is
                // never set wrongly, it is only set incompletely.
                result[e] = e.IsCritical || (threshold > 0 && e.Amount > threshold);
            }
        }

        foreach (DamageEvent e in events.Where(e => e.IsHeal))
        {
            result[e] = false;
        }

        return result;
    }

    /// <summary>Returns 0 when the group is too small to judge, which the caller reads as "no
    /// hit here counts as a crit".</summary>
    private static double ThresholdFor(List<long> amounts)
    {
        if (amounts.Count < MinimumSampleSize)
        {
            return 0;
        }

        double median = Median(amounts);

        // Median of the lower half rather than of everything: with a 19% crit rate the overall
        // median already sits among the normal hits, but the lower half is free of crits
        // entirely, so its median is a cleaner estimate of what a normal hit does.
        var lower = amounts.Where(a => a <= median).ToList();
        return lower.Count == 0 ? 0 : Median(lower) * CritThresholdFactor;
    }

    private static double Median(List<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}
