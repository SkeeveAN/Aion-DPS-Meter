namespace AionDPS.Combat;

/// <summary>One skill's raw usage counts for one player against one target/window - shared between
/// the Player Details view and the backend upload payload so both read the exact same numbers.</summary>
public sealed record SkillUsage(string Skill, int Hits, int CritHits, long Total, long Min, long Max, int SkillId = 0);

/// <summary>
/// Groups a player's damage events by skill and counts hits/crits/totals per group. Extracted from
/// <see cref="AionDPS.Ui.PlayerDetailsWindow"/> so the backend upload (Backend/UploadClient.cs)
/// computes crit rates the same way the UI already shows them, rather than a second, potentially
/// diverging implementation.
/// </summary>
public static class SkillBreakdown
{
    /// <summary>Groups the events by skill. Aion 2 flags every crit in the packet itself, so the crit
    /// count is simply the flagged events. <paramref name="heals"/> selects which half of
    /// <paramref name="events"/> to group: false (the default) damage, true heals.</summary>
    public static List<SkillUsage> For(IReadOnlyList<DamageEvent> events, bool heals = false)
    {
        var relevant = events.Where(e => e.IsHeal == heals).ToList();

        return relevant
            .GroupBy(e => e.Skill ?? "(auto attack)")
            .Select(g =>
            {
                // Damage-over-time ticks add to the total but are not hits: hit count, crit rate
                // and min/max describe the casts. A skill seen only through its ticks (cast before
                // the meter started) still shows them, so its row is never empty.
                var hits = g.Where(e => !e.IsTick).ToList();
                var amounts = (hits.Count > 0 ? hits : g.ToList()).Select(e => e.Amount).ToList();
                int crits = hits.Count(e => e.IsCritical);
                return new SkillUsage(g.Key, amounts.Count, crits, g.Sum(e => e.Amount), amounts.Min(), amounts.Max(),
                    g.Select(e => e.SkillId).FirstOrDefault(id => id != 0));
            })
            .OrderByDescending(s => s.Total)
            .ToList();
    }
}
