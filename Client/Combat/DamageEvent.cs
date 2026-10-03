namespace AionDPS.Combat;

/// <summary>
/// One damage or heal instance, already decoded from whatever produced it. Deliberately
/// source-agnostic: the DPS/iDPS math is built and verified against synthetic events, and this
/// type is what keeps the calculator and the aggregator independent of the input.
/// </summary>
/// <param name="Skill">The ability used, when the line named one. Null for an auto-attack, and
/// also for the several line shapes that carry a number but no skill. Optional so every existing
/// construction site -- demo data, selftests -- keeps compiling and simply reports no skill.</param>
/// <param name="IsCritical">Whether the client marked the line as a critical hit. Reliable for the
/// LOCAL player only: measured across four logs of one fight, an observer's client marks roughly
/// half of another player's crits (8,8% against the 19,0% that player's own client recorded), so a
/// crit rate shown for anyone else is a floor, not a rate.</param>
/// <param name="IsTick">A damage-over-time tick rather than a hit: it adds to the damage, but not to
/// the hit count or crit rate (the Aion 2 combat analyzer counts a DoT skill's casts as its hits).</param>
public readonly record struct DamageEvent(
    DateTime Timestamp,
    int SourceObjectId,
    int TargetObjectId,
    long Amount,
    bool IsHeal,
    string? Skill = null,
    bool IsCritical = false,
    bool IsTick = false,
    int SkillId = 0);
