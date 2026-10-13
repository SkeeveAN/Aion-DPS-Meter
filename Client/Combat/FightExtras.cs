namespace AionDPS.Combat;

/// <summary>A skill an entity used (the game's cast notice), for the rotation view.</summary>
public readonly record struct SkillUse(DateTime At, int Actor, int SkillId);

public enum BuffKind
{
    On,
    Renewed,
    End,
}

/// <summary>An effect put on, renewed on or taken off an entity. <see cref="DurationMs"/> is -1 for one without an end; an <see cref="BuffKind.End"/> carries no skill.</summary>
public readonly record struct BuffEvent(DateTime At, BuffKind Kind, int Target, int Instance, int SkillId, int Caster, int DurationMs);

/// <summary>A reading of a monster's stagger bar (<see cref="Current"/> of <see cref="Max"/>), or the moment it broke.</summary>
public readonly record struct StaggerEvent(DateTime At, int Entity, int Current, int Max, bool Broke);

/// <summary>Where an entity was during a fight: the time of each point as milliseconds from <see cref="Start"/>, the place in centimetres.</summary>
public sealed record PositionTrack(int Entity, DateTime Start, int[] T, int[] X, int[] Y);

/// <summary>A monster winding a skill up (the game's windup frame): where, aimed at whom, for how long. The replay draws its area while it lasts.</summary>
public readonly record struct WindupRecord(DateTime At, int Actor, int SkillId, int Target, int DurationMs, double Facing, int X, int Y, bool HasPosition);

public readonly record struct DeathRecord(DateTime At, int Entity, DateTime? RevivedAt = null);

/// <summary>An entity's hit points over time (a boss's), in thousandths, the time as milliseconds from <see cref="Start"/>.</summary>
public sealed record HpSeries(int Entity, DateTime Start, int[] T, short[] Permille);

/// <summary>
/// What the packet stream said during a fight besides damage: the skills used, the effects on everyone, the bosses' stagger and - for the replay - where the group and the
/// bosses were, the skills the bosses wound up, who died and the bosses' health. Kept with a stored fight (<see cref="History.ExtrasBlob"/>) so the views can show an old fight
/// too. <see cref="Names"/> names the ids that occur in it.
/// </summary>
public sealed record FightExtras(
    IReadOnlyList<SkillUse> Uses,
    IReadOnlyList<BuffEvent> Buffs,
    IReadOnlyList<StaggerEvent> Staggers,
    IReadOnlyDictionary<int, string> Names,
    IReadOnlyList<PositionTrack>? Tracks = null,
    IReadOnlyList<WindupRecord>? Windups = null,
    IReadOnlyList<DeathRecord>? Deaths = null,
    IReadOnlyList<HpSeries>? Hp = null)
{
    public static FightExtras Empty { get; } = new(Array.Empty<SkillUse>(), Array.Empty<BuffEvent>(), Array.Empty<StaggerEvent>(), new Dictionary<int, string>());

    public bool IsEmpty => Uses.Count == 0 && Buffs.Count == 0 && Staggers.Count == 0 && (Tracks is null || Tracks.Count == 0) && (Windups is null || Windups.Count == 0);

    /// <summary>The same data under other ids (a stored fight is shown under the ids of the session that shows it).</summary>
    public FightExtras Remap(Func<int, int> map) => new(
        Uses.Select(u => u with { Actor = map(u.Actor) }).ToList(),
        Buffs.Select(b => b with { Target = map(b.Target), Caster = map(b.Caster) }).ToList(),
        Staggers.Select(s => s with { Entity = map(s.Entity) }).ToList(),
        Names.ToDictionary(kv => map(kv.Key), kv => kv.Value),
        Tracks?.Select(t => t with { Entity = map(t.Entity) }).ToList(),
        Windups?.Select(w => w with { Actor = map(w.Actor), Target = map(w.Target) }).ToList(),
        Deaths?.Select(d => d with { Entity = map(d.Entity) }).ToList(),
        Hp?.Select(h => h with { Entity = map(h.Entity) }).ToList());
}
