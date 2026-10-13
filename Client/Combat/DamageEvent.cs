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
/// <param name="Kinds">What the hit's own data says about it (see <see cref="HitKinds">); null when it says nothing (heals, ticks, hits from a fight stored before this was read).</param>
/// <param name="HitCount">The number of strikes in one hit frame (a multi-hit skill sends 2..5 in one).</param>
/// <param name="Estimated">The amount is not the game's number but a guess: on a crowded world boss the server sends no damage figure for other players, only the notice that a hit landed (who, whom, which skill). The amount is then the average hit of that skill (see Aion2.Protocol.Aion2HitAverages). Never set for the hits the game does report in full.</param>
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
    int SkillId = 0,
    HitKinds? Kinds = null,
    int HitCount = 1,
    bool Estimated = false);

/// <summary>
/// What a hit frame says about how the hit landed, read from its flag bytes (the first byte after the damage type is a bit set: 2, 4, 8; the third says where from: 1 or 2).
/// Checked against the damage it did, over the skills of a debug recording (2026-10-12; a Double hit did about 2.2 times the damage of the same skill without it, a Parry
/// about half, a Crit 1.7 times, a Back hit 1.13 times, a Front hit 0.91 times); not yet checked against the game's own combat log.
/// </summary>
[Flags]
public enum HitKinds
{
    None = 0,
    Parry = 1 << 0,
    Perfect = 1 << 1,
    Double = 1 << 2,
    Back = 1 << 3,
    Front = 1 << 4,
}
