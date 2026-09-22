using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>Per-player defensive tally: how many incoming attacks were avoided, by kind, against
/// how many landed - the rate a tank or a PvP player actually wants to see.</summary>
public sealed record DefenseSummary(int Dodges, int Parries, int Blocks, int Resists, int HitsTaken)
{
    public int Avoided => Dodges + Parries + Blocks + Resists;

    public int IncomingAttacks => Avoided + HitsTaken;

    /// <summary>Share of incoming attacks that did no damage; null with no incoming attacks at all.</summary>
    public double? AvoidRatePercent => IncomingAttacks == 0 ? null : 100.0 * Avoided / IncomingAttacks;

    public static DefenseSummary Empty { get; } = new(0, 0, 0, 0, 0);

    /// <summary>Compact one-line form for a grid cell: "D 3 · P 12 · B 8 · R 2 (41%)" - only the
    /// kinds that occurred, blank when nothing was ever aimed at this player.</summary>
    public string Display
    {
        get
        {
            if (IncomingAttacks == 0)
            {
                return "";
            }

            var parts = new List<string>(4);
            if (Dodges > 0) parts.Add($"D {Dodges}");
            if (Parries > 0) parts.Add($"P {Parries}");
            if (Blocks > 0) parts.Add($"B {Blocks}");
            if (Resists > 0) parts.Add($"R {Resists}");
            string rate = AvoidRatePercent is double r ? $" ({r:F0}%)" : "";
            return parts.Count == 0 ? $"0 avoided{rate}" : string.Join(" · ", parts) + rate;
        }
    }
}

public static class DefenseStats
{
    /// <summary>Tallies avoids and landed hits per TARGET (the defender) inside the given window.
    /// Heals and hits with no defender of interest are ignored by the caller's filtering.</summary>
    public static Dictionary<int, DefenseSummary> ByDefender(IEnumerable<AvoidEvent> avoids, IEnumerable<DamageEvent> hits)
    {
        var tally = new Dictionary<int, (int D, int P, int B, int R, int H)>();

        foreach (AvoidEvent avoid in avoids)
        {
            (int d, int p, int b, int r, int h) = tally.GetValueOrDefault(avoid.TargetObjectId);
            switch (avoid.Kind)
            {
                case AvoidKind.Dodge: d++; break;
                case AvoidKind.Parry: p++; break;
                case AvoidKind.Block: b++; break;
                case AvoidKind.Resist: r++; break;
            }
            tally[avoid.TargetObjectId] = (d, p, b, r, h);
        }

        foreach (DamageEvent hit in hits)
        {
            if (hit.IsHeal)
            {
                continue;
            }

            (int d, int p, int b, int r, int h) = tally.GetValueOrDefault(hit.TargetObjectId);
            tally[hit.TargetObjectId] = (d, p, b, r, h + 1);
        }

        return tally.ToDictionary(kv => kv.Key, kv => new DefenseSummary(kv.Value.D, kv.Value.P, kv.Value.B, kv.Value.R, kv.Value.H));
    }
}
