namespace AionSniffer.Combat;

/// <summary>
/// Pure DPS/iDPS math, deliberately separated from any live capture state so it can be
/// verified against synthetic data. Definitions as settled on (see README "DPS vs. iDPS"):
///
/// - "DPS" (the ALL/open-world view) = a source's damage across however many different
///   targets it hit, divided by elapsed time. Ambiguous on purpose: whether idle gaps
///   between fights count is a real design choice, not a bug -- both variants are exposed.
/// - "iDPS" = damage to ONE target, divided by that target's engagement window (first hit
///   to last hit across the whole group), i.e. the same shared duration for every source
///   that participated. Verified against real myaion.eu numbers: all 6 players in a boss
///   fight resolved to the same ~192.4s duration, not an individually-measured one -- an
///   individual timer would unfairly reward burst players with long downtime between casts.
/// - Instance-level iDPS is the natural extension: sum(damage) / sum(duration) across
///   multiple such single-target encounters (a weighted average, not a mean of per-boss DPS).
/// </summary>
public static class DpsCalculator
{
    /// <summary>
    /// "ALL" view, wall-clock variant: total damage a source dealt (to any target) divided by
    /// the time between its first and last hit, gaps and all. Well-defined for any set of hits,
    /// but a source with long idle stretches between fights looks worse than reality.
    /// </summary>
    public static double AllDpsWallClock(IReadOnlyList<DamageEvent> events, int sourceObjectId)
    {
        var hits = HitsBy(events, sourceObjectId);
        if (hits.Count == 0)
        {
            return 0;
        }

        long total = hits.Sum(e => e.Amount);
        double seconds = (hits[^1].Timestamp - hits[0].Timestamp).TotalSeconds;
        return seconds > 0 ? total / seconds : total;
    }

    /// <summary>
    /// "ALL" view, active-only variant: same total damage, but the denominator only counts
    /// inter-hit gaps up to <paramref name="idleThreshold"/>. Gaps longer than that (walking
    /// between mobs, waiting for a pull) are excluded entirely. Convention: the tail after the
    /// very last hit of a stretch is not counted either, so this slightly undercounts on
    /// purpose rather than guess at a "still fighting" grace period.
    /// </summary>
    public static double AllDpsActiveOnly(IReadOnlyList<DamageEvent> events, int sourceObjectId, TimeSpan idleThreshold)
    {
        var hits = HitsBy(events, sourceObjectId);
        if (hits.Count == 0)
        {
            return 0;
        }

        long total = hits.Sum(e => e.Amount);
        double activeSeconds = 0;
        for (int i = 1; i < hits.Count; i++)
        {
            var gap = hits[i].Timestamp - hits[i - 1].Timestamp;
            if (gap <= idleThreshold)
            {
                activeSeconds += gap.TotalSeconds;
            }
        }

        return activeSeconds > 0 ? total / activeSeconds : total;
    }

    /// <summary>
    /// iDPS for one target: damage to that target (optionally filtered to one source) divided
    /// by the target's engagement window -- first hit to last hit, across ALL sources that hit
    /// it. That shared window is what makes it fair between a burst caster and a sustained
    /// melee (see class remarks). Null if the target was never hit, or was hit only once
    /// (zero-length window, no rate to compute).
    /// </summary>
    public static double? TargetIDps(IReadOnlyList<DamageEvent> events, int targetObjectId, int? sourceObjectId = null)
    {
        var targetHits = events.Where(e => e.TargetObjectId == targetObjectId && !e.IsHeal).ToList();
        if (targetHits.Count == 0)
        {
            return null;
        }

        double duration = (targetHits.Max(e => e.Timestamp) - targetHits.Min(e => e.Timestamp)).TotalSeconds;
        if (duration <= 0)
        {
            return null;
        }

        long damage = sourceObjectId is int src
            ? targetHits.Where(e => e.SourceObjectId == src).Sum(e => e.Amount)
            : targetHits.Sum(e => e.Amount);

        return damage / duration;
    }

    /// <summary>
    /// Instance-level iDPS: weighted average across multiple single-target encounters --
    /// sum(damage) / sum(duration), NOT the mean of each encounter's own iDPS (a long, low-DPS
    /// fight should not count as much as a short, high-DPS one, but neither should it count
    /// for nothing).
    /// </summary>
    public static double? InstanceIDps(IEnumerable<(long Damage, TimeSpan Duration)> perEncounterTotals)
    {
        long totalDamage = 0;
        double totalSeconds = 0;
        foreach (var (damage, duration) in perEncounterTotals)
        {
            totalDamage += damage;
            totalSeconds += duration.TotalSeconds;
        }

        return totalSeconds > 0 ? totalDamage / totalSeconds : null;
    }

    private static List<DamageEvent> HitsBy(IReadOnlyList<DamageEvent> events, int sourceObjectId) =>
        events.Where(e => e.SourceObjectId == sourceObjectId && !e.IsHeal)
              .OrderBy(e => e.Timestamp)
              .ToList();
}
