using AionSniffer.Protocol;

namespace AionSniffer.Combat;

/// <summary>
/// Turns decoded SM_ATTACK packets into DamageEvents and keeps a running per-source damage
/// tally, so the calibration tool can show live DPS numbers once the opcodes are confirmed --
/// not just raw packet dumps. SM_ATTACK_STATUS is deliberately not fed in here: in this
/// server-build's layout it carries no attacker field at all (see CombatPacketParser), so its
/// HP/MP ticks can't be attributed to a source and would only pollute per-player numbers.
/// </summary>
public sealed class LiveAggregator
{
    private readonly List<DamageEvent> _events = new();

    public IReadOnlyList<DamageEvent> Events => _events;

    public void IngestAttack(DateTime timestamp, CombatPacketParser.AttackPacket attack)
    {
        foreach (var hit in attack.Hits)
        {
            if (hit.Damage > 0)
            {
                _events.Add(new DamageEvent(timestamp, attack.AttackerObjectId, attack.TargetObjectId, hit.Damage, IsHeal: false));
            }
        }
    }

    /// <summary>One-line-per-source leaderboard for the console dump: total damage and wall-clock
    /// "ALL" DPS since the first hit seen for that source (see DpsCalculator remarks on why this is
    /// the ambiguous, gap-inclusive variant rather than iDPS).</summary>
    public string Summarize()
    {
        if (_events.Count == 0)
        {
            return "(no attributable damage yet)";
        }

        var lines = _events
            .GroupBy(e => e.SourceObjectId)
            .Select(g => (SourceId: g.Key, Total: g.Sum(e => e.Amount), Dps: DpsCalculator.AllDpsWallClock(_events, g.Key)))
            .OrderByDescending(x => x.Total)
            .Select(x => $"0x{x.SourceId:X8}: {x.Total} dmg ({(x.Dps is double d ? d.ToString("F0") : "n/a")} DPS)");

        return string.Join(" | ", lines);
    }
}
