using AionDPS.Combat.Sources;

namespace AionDPS.Combat;

/// <summary>One player's PvP record inside a window: kills of other players, own deaths, and the
/// hardest single hit landed on a player.</summary>
public sealed record PvpSummary(int Kills, int Deaths, long MaxHit)
{
    public static PvpSummary Empty { get; } = new(0, 0, 0);

    public string KdDisplay => Deaths == 0 ? (Kills == 0 ? "" : $"{Kills}/0") : $"{Kills}/{Deaths} ({(double)Kills / Deaths:F1})";

    /// <summary>"K 3 / D 1 · max 12,345" - blank for a player with no PvP record at all.</summary>
    public string Display
    {
        get
        {
            if (Kills == 0 && Deaths == 0 && MaxHit == 0)
            {
                return "";
            }

            string kd = $"K {Kills} / D {Deaths}";
            return MaxHit > 0 ? $"{kd} · max {MaxHit:N0}" : kd;
        }
    }
}

public static class PvpStats
{
    /// <summary>
    /// Kills count for the killer only when the victim was a player; deaths count for any player
    /// victim regardless of what killed them (a mob death is still a death). MaxHit is the largest
    /// damage event whose target is a player, per attacker.
    /// </summary>
    public static Dictionary<int, PvpSummary> ByPlayer(IEnumerable<KillEvent> kills, IEnumerable<DamageEvent> hits, Func<int, bool> isPlayer)
    {
        var tally = new Dictionary<int, (int K, int D, long Max)>();

        foreach (KillEvent kill in kills)
        {
            if (kill.VictimIsPlayer || isPlayer(kill.VictimObjectId))
            {
                (int k, int d, long m) = tally.GetValueOrDefault(kill.VictimObjectId);
                tally[kill.VictimObjectId] = (k, d + 1, m);

                // A mob finishing a player is that player's death, not anyone's PvP kill.
                if (kill.KillerObjectId is int killer && killer != kill.VictimObjectId && isPlayer(killer))
                {
                    (int kk, int kd, long km) = tally.GetValueOrDefault(killer);
                    tally[killer] = (kk + 1, kd, km);
                }
            }
        }

        foreach (DamageEvent hit in hits)
        {
            if (hit.IsHeal || !isPlayer(hit.TargetObjectId) || hit.SourceObjectId == hit.TargetObjectId)
            {
                continue;
            }

            (int k, int d, long m) = tally.GetValueOrDefault(hit.SourceObjectId);
            tally[hit.SourceObjectId] = (k, d, Math.Max(m, hit.Amount));
        }

        return tally.ToDictionary(kv => kv.Key, kv => new PvpSummary(kv.Value.K, kv.Value.D, kv.Value.Max));
    }
}
