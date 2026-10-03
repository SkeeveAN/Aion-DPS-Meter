namespace AionDPS.Combat;

/// <summary>
/// A boss fight is the boss and its adds: everything the group hits from the first to the last
/// blow on the boss counts toward the fight, the way players read a boss's DPS. Kept apart from
/// the window so the rules can be checked on their own (see SelfCheckAion2).
/// </summary>
public static class BossFight
{
    /// <summary>
    /// The damage shown for one selected target. For a boss: every hit on the boss or on a monster
    /// (an add) between the first and the last hit on the boss inside the run window. For anything
    /// else: the hits on that target inside the run window, as before.
    /// </summary>
    /// <param name="damage">Damage events only (no heals).</param>
    /// <param name="from">Run window start, or null for the target's whole history.</param>
    /// <param name="to">Run window end (null with <paramref name="from"/>).</param>
    /// <param name="isBoss">True for a known boss.</param>
    /// <param name="isMonster">True for a monster that is nobody's summon and no player.</param>
    public static List<DamageEvent> ShownHits(IEnumerable<DamageEvent> damage, int targetId, DateTime? from, DateTime? to,
        Func<int, bool> isBoss, Func<int, bool> isMonster)
    {
        var all = damage as IReadOnlyList<DamageEvent> ?? damage.ToList();
        var onTarget = all.Where(ev => ev.TargetObjectId == targetId && InWindow(ev, from, to)).ToList();
        if (onTarget.Count == 0 || !isBoss(targetId))
        {
            return onTarget;
        }

        DateTime first = onTarget.Min(ev => ev.Timestamp);
        DateTime last = onTarget.Max(ev => ev.Timestamp);
        return all.Where(ev => ev.Timestamp >= first && ev.Timestamp <= last
                && (ev.TargetObjectId == targetId || isMonster(ev.TargetObjectId)))
            .ToList();
    }

    /// <summary>DPS over the fight: the source's damage in <paramref name="shown"/> over the time
    /// from the first to the last hit on the target (the boss, for a boss fight).</summary>
    public static double? Dps(IReadOnlyList<DamageEvent> shown, int targetId, int sourceId)
    {
        DateTime? first = null, last = null;
        long damage = 0;
        foreach (DamageEvent ev in shown)
        {
            if (ev.TargetObjectId == targetId)
            {
                first = first is null || ev.Timestamp < first ? ev.Timestamp : first;
                last = last is null || ev.Timestamp > last ? ev.Timestamp : last;
            }

            if (ev.SourceObjectId == sourceId)
            {
                damage += ev.Amount;
            }
        }

        double seconds = first is DateTime a && last is DateTime b ? (b - a).TotalSeconds : 0;
        return seconds > 0 ? damage / seconds : null;
    }

    /// <summary>
    /// Whether this batch starts a new boss pull, so the meter should start from zero: it hits a
    /// boss that has no hit on record yet, or one whose health was reset to full (a wipe) after its
    /// last recorded hit. Not while another boss is hurt and alive (two bosses at once).
    /// </summary>
    /// <param name="recorded">The meter's damage on record.</param>
    /// <param name="lastResetOf">The last time a boss was reset to full health, if ever.</param>
    /// <param name="otherBossFightUnfinished">True while a boss other than this one is hurt and alive.</param>
    public static bool StartsNewPull(IReadOnlyList<DamageEvent> batch, IReadOnlyList<DamageEvent> recorded,
        Func<int, bool> isBoss, Func<int, DateTime?> lastResetOf, Func<int, bool> otherBossFightUnfinished)
    {
        if (recorded.Count == 0)
        {
            return false;
        }

        foreach (int boss in batch.Where(ev => !ev.IsHeal).Select(ev => ev.TargetObjectId).Distinct().Where(isBoss))
        {
            DateTime? lastHit = null;
            for (int i = recorded.Count - 1; i >= 0; i--)
            {
                if (!recorded[i].IsHeal && recorded[i].TargetObjectId == boss)
                {
                    lastHit = recorded[i].Timestamp;
                    break;
                }
            }

            bool fresh = lastHit is null || lastResetOf(boss) is DateTime reset && reset > lastHit;
            if (fresh && !otherBossFightUnfinished(boss))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InWindow(DamageEvent ev, DateTime? from, DateTime? to) =>
        from is not DateTime start || (ev.Timestamp >= start && ev.Timestamp <= (to ?? DateTime.MaxValue));
}
