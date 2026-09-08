namespace AionSniffer.Combat;

/// <summary>
/// One damage or heal instance, already decoded from whatever produced it. Deliberately
/// source-agnostic: the DPS/iDPS math is built and verified against synthetic events, and this
/// type is what let the meter switch from decoded packets to parsed Chat.log lines without the
/// calculator or the aggregator changing at all.
/// </summary>
/// <param name="Skill">The ability used, when the line named one. Null for an auto-attack, and
/// also for the several line shapes that carry a number but no skill. Optional so every existing
/// construction site -- demo data, selftests -- keeps compiling and simply reports no skill.</param>
/// <param name="IsCritical">Whether the client marked the line as a critical hit. Reliable for the
/// LOCAL player only: measured across four logs of one fight, an observer's client marks roughly
/// half of another player's crits (8,8% against the 19,0% that player's own client recorded), so a
/// crit rate shown for anyone else is a floor, not a rate.</param>
public readonly record struct DamageEvent(
    DateTime Timestamp,
    int SourceObjectId,
    int TargetObjectId,
    long Amount,
    bool IsHeal,
    string? Skill = null,
    bool IsCritical = false);
