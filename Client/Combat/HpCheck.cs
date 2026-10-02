namespace AionDPS.Combat;

/// <summary>How the damage counted against a target compares with what the target really lost.</summary>
public enum HpCheckVerdict
{
    /// <summary>Within <see cref="HpCheck.Tolerance"/> of the hit points lost.</summary>
    Match,
    /// <summary>Less damage counted than the target lost - hits the meter did not see.</summary>
    Missing,
    /// <summary>More damage counted than the target lost - something counted twice or wrongly.</summary>
    Excess,
}

/// <param name="Lost">Hit points the target lost between the first and the last reading inside the
/// run (the killing blow's overkill is left out: the last reading used is the last one above 0).</param>
/// <param name="Counted">Damage the meter counted over that same stretch.</param>
/// <param name="Killed">The target's last reading in the run is 0.</param>
/// <param name="Highest">The highest hit points seen for the target - its full health once it has
/// been seen full.</param>
/// <param name="RunTotal">All damage counted in the run, overkill included.</param>
/// <param name="Overkill">Damage past the target's last hit points, when it died.</param>
/// <param name="Shielded">Damage dealt while the target's hit points stood still (a boss's shield
/// phase, see <see cref="HpCheck.FrozenGap"/>) - counted by the meter as by the game, left out of
/// the comparison.</param>
public sealed record HpCheckResult(long Lost, long Counted, bool Killed, long Highest, long RunTotal, long Overkill, HpCheckVerdict Verdict, long Shielded = 0)
{
    public double Ratio => Lost > 0 ? (double)Counted / Lost : 0;

    /// <summary>The run's total, overkill aside, is more than the target could ever lose - the
    /// "1300 % of the boss" symptom, whatever its cause.</summary>
    public bool OverFullHealth => Highest > 0 && RunTotal - Overkill - Shielded > Highest * (1 + HpCheck.Tolerance);
}

/// <summary>
/// The guard against wrong totals: the damage the meter counted against a target, held against the
/// hit points the server says it lost. Readings are taken from the server's own hit-point frames,
/// so a match says every hit was seen and none was counted twice; on the Aion 2 captures it is used
/// with, a boss's hit points plus the damage counted against it stay constant to the point.
/// </summary>
public static class HpCheck
{
    /// <summary>Accepted difference, as a share of the hit points lost. Readings and hits that
    /// arrive in one network packet share a timestamp, so a hit can land on either side of a
    /// reading at the edges of the stretch; 1 % covers that and nothing a real gap would hide.</summary>
    public const double Tolerance = 0.01;

    /// <summary>
    /// Readings this far apart while the target is being hit mean its hit points stood still: the
    /// server sends one per change, every 50-100 ms under fire. Transcendent Bakarma (Draupnir) has
    /// such a phase - 15 s, 252,502 damage shown, 976 HP lost - and the game's combat analyzer
    /// counts that damage (785,570 for one player who dealt 75,802 of it; capture 2026-10-02 23:00).
    /// The meter counts it too; the check sets it aside.
    /// </summary>
    public static readonly TimeSpan FrozenGap = TimeSpan.FromSeconds(3);

    /// <summary>Null when there is nothing to compare: fewer than two readings inside the run, or
    /// no hit points lost between them.</summary>
    /// <param name="readings">The target's hit-point readings, oldest first, covering the run.</param>
    /// <param name="runHits">The damage events of the run against this target.</param>
    public static HpCheckResult? Evaluate(IReadOnlyList<(DateTime At, long Hp)> readings, IReadOnlyList<DamageEvent> runHits, long highest)
    {
        if (runHits.Count == 0)
        {
            return null;
        }

        DateTime start = runHits.Min(h => h.Timestamp);
        DateTime end = runHits.Max(h => h.Timestamp);
        var inside = readings.Where(r => r.At >= start && r.At <= end).ToList();
        bool killed = inside.Count > 0 && inside[^1].Hp == 0;

        // From the first reading to the last one above 0: every hit between them shows up in the
        // readings in full, the killing blow's overkill does not.
        var alive = inside.Where(r => r.Hp > 0).ToList();
        if (alive.Count < 2)
        {
            return null;
        }

        (DateTime From, long Hp) first = alive[0];
        (DateTime To, long Hp) last = alive[^1];
        long lost = first.Hp - last.Hp;
        if (lost <= 0)
        {
            return null;
        }

        // Stretches with no reading for a while: the hit points stood still (FrozenGap). What was
        // dealt and lost across them is set aside on both sides.
        var frozen = new List<(DateTime From, DateTime To)>();
        for (int i = 1; i < alive.Count; i++)
        {
            if (alive[i].At - alive[i - 1].At >= FrozenGap)
            {
                frozen.Add((alive[i - 1].At, alive[i].At));
                lost -= alive[i - 1].Hp - alive[i].Hp;
            }
        }

        bool InFrozen(DamageEvent h) => frozen.Any(f => h.Timestamp > f.From && h.Timestamp <= f.To);
        long shielded = runHits.Where(h => !h.IsHeal && InFrozen(h)).Sum(h => h.Amount);
        if (lost <= 0)
        {
            return null;
        }

        long counted = runHits.Where(h => !h.IsHeal && h.Timestamp > first.From && h.Timestamp <= last.To && !InFrozen(h)).Sum(h => h.Amount);
        double ratio = (double)counted / lost;
        HpCheckVerdict verdict = ratio > 1 + Tolerance ? HpCheckVerdict.Excess
            : ratio < 1 - Tolerance ? HpCheckVerdict.Missing
            : HpCheckVerdict.Match;

        long runTotal = runHits.Where(h => !h.IsHeal).Sum(h => h.Amount);
        long overkill = killed ? Math.Max(0, runHits.Where(h => !h.IsHeal && h.Timestamp > last.To).Sum(h => h.Amount) - last.Hp) : 0;
        return new HpCheckResult(lost, counted, killed, highest, runTotal, overkill, verdict, shielded);
    }
}
