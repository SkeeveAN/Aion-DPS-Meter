namespace AionSniffer.Combat;

/// <summary>One skill's raw usage counts for one player against one target/window - shared between
/// the Player Details view and the backend upload payload so both read the exact same numbers.</summary>
public sealed record SkillUsage(string Skill, int Hits, int CritHits, long Total, long Min, long Max);

/// <summary>
/// Groups a player's damage events by skill and counts hits/crits/totals per group. Extracted from
/// <see cref="AionSniffer.Ui.PlayerDetailsWindow"/> so the backend upload (Backend/UploadClient.cs)
/// computes crit rates the same way the UI already shows them, rather than a second, potentially
/// diverging implementation.
/// </summary>
public static class SkillBreakdown
{
    /// <summary><paramref name="trustLoggedFlag"/> is for the local player, whose client flags its
    /// own crits properly - see <see cref="CritEstimator"/>'s remarks for why that never holds for
    /// anyone else.</summary>
    public static List<SkillUsage> For(IReadOnlyList<DamageEvent> events, bool trustLoggedFlag)
    {
        var damage = events.Where(e => !e.IsHeal).ToList();
        var isCrit = CritEstimator.Estimate(damage, trustLoggedFlag);

        return damage
            .GroupBy(e => e.Skill ?? "(auto attack)")
            .Select(g =>
            {
                var amounts = g.Select(e => e.Amount).ToList();
                int crits = g.Count(e => isCrit.GetValueOrDefault(e));
                return new SkillUsage(g.Key, amounts.Count, crits, amounts.Sum(), amounts.Min(), amounts.Max());
            })
            .OrderByDescending(s => s.Total)
            .ToList();
    }
}
