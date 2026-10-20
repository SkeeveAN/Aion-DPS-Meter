namespace AionDPS.Combat;

/// <summary>
/// The final correction of a world boss's estimated damage. While the fight runs, the decoder scales every estimated hit with what the boss
/// had lost at that moment (Aion2FrameDecoder.EstimateScale): that needs a few hundred estimates first, runs behind the hit points, and
/// never goes below the table's value - so a short crowd fight ends at 80 % of the boss's hit points, a small group's at 115 % (aiondps.com,
/// 2026-10-20: five world bosses between 81 and 115 %). Once the fight is over the loss is known exactly, and the estimates are scaled once
/// more, all by the same factor: the players keep their order and the sum is what the boss really lost.
/// </summary>
public static class EstimateRescale
{
    /// <summary>Estimated hits needed before the factor means anything (fewer, and the figure stays the decoder's).</summary>
    public const int MinEstimatedHits = 20;

    /// <summary>The bounds of the factor: the live estimate was never far off, a wild reading must not blow it up.</summary>
    public const double MinFactor = 0.25, MaxFactor = 4.0;

    /// <summary>The factor the estimated hits are to be multiplied with so that exact plus estimated damage equals <paramref name="lost"/>, or null when there is nothing to correct.</summary>
    public static double? FactorFor(IReadOnlyList<DamageEvent> targetHits, long lost)
    {
        long exact = 0, estimated = 0;
        int estimatedHits = 0;
        foreach (DamageEvent e in targetHits)
        {
            if (e.IsHeal)
            {
                continue;
            }

            if (e.Estimated)
            {
                estimated += e.Amount;
                estimatedHits++;
            }
            else
            {
                exact += e.Amount;
            }
        }

        if (estimatedHits < MinEstimatedHits || estimated <= 0 || lost <= exact)
        {
            return null;
        }

        return Math.Clamp((double)(lost - exact) / estimated, MinFactor, MaxFactor);
    }

    /// <summary>The hits with every estimated amount scaled by <paramref name="factor"/>; exact hits stay as they are.</summary>
    public static List<DamageEvent> Apply(IReadOnlyList<DamageEvent> targetHits, double factor) =>
        targetHits.Select(e => e.Estimated && !e.IsHeal ? e with { Amount = Math.Max(1, (long)Math.Round(e.Amount * factor)) } : e).ToList();
}
