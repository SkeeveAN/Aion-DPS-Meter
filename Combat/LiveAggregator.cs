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

    /// <summary>
    /// Feeds already-decoded DamageEvents straight in -- the ChatLog path's entry point
    /// (AionSniffer.ChatLog.ChatLogParser produces DamageEvents directly, there is no
    /// protocol-specific AttackPacket to unwrap like the network path's IngestAttack).
    /// </summary>
    public void IngestEvents(IEnumerable<DamageEvent> events) => _events.AddRange(events);

    /// <summary>
    /// Discards all events for a fresh session. Previously missing on purpose-turned-oversight:
    /// the GUI's "Clear" button used to only clear its own row list, leaving the underlying events
    /// in place -- so DpsCalculator kept averaging in pre-clear damage into any "new" numbers
    /// instead of starting from zero, invisibly, since the row list looked empty right after.
    /// </summary>
    public void Clear() => _events.Clear();

    /// <summary>One-line-per-source leaderboard for the console dump: total damage and wall-clock
    /// "ALL" DPS since the first hit seen for that source (see DpsCalculator remarks on why this is
    /// the ambiguous, gap-inclusive variant rather than iDPS). <paramref name="nameResolver"/> lets
    /// the chat-log path show real names (it has them for free from Chat.log) instead of the raw
    /// synthetic/hex object id the network path is stuck with until SM_GROUP_MEMBER_INFO is wired up.</summary>
    public string Summarize(Func<int, string?>? nameResolver = null)
    {
        // Found by terminal_windows against a real, heal-bearing Chat.log session: this used to
        // group ALL events (heals included) while the Dps column below already filtered heals out
        // via DpsCalculator.HitsBy -- so a pure healer showed up as "Potion: 3526 dmg (n/a DPS)",
        // a damage total with no rate to match it, because the total and the rate were silently
        // computed over two different event sets. The network path never triggered this (its
        // AttackPackets are always IsHeal: false), only the chat-log path's real heal lines did.
        var damageEvents = _events.Where(e => !e.IsHeal).ToList();
        if (damageEvents.Count == 0)
        {
            return "(no attributable damage yet)";
        }

        var lines = damageEvents
            .GroupBy(e => e.SourceObjectId)
            .Select(g => (SourceId: g.Key, Total: g.Sum(e => e.Amount), Dps: DpsCalculator.AllDpsWallClock(damageEvents, g.Key)))
            .OrderByDescending(x => x.Total)
            .Select(x => $"{nameResolver?.Invoke(x.SourceId) ?? $"0x{x.SourceId:X8}"}: {x.Total} dmg ({(x.Dps is double d ? d.ToString("F0") : "n/a")} DPS)");

        return string.Join(" | ", lines);
    }
}
