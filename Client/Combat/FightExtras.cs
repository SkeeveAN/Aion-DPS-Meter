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

/// <summary>
/// What the packet stream said during a fight besides damage: the skills used, the effects on everyone and the bosses' stagger. Kept with a stored fight
/// (<see cref="History.ExtrasBlob"/>) so the rotation and buff views can show an old fight too. <see cref="Names"/> names the ids that occur in it.
/// </summary>
public sealed record FightExtras(
    IReadOnlyList<SkillUse> Uses,
    IReadOnlyList<BuffEvent> Buffs,
    IReadOnlyList<StaggerEvent> Staggers,
    IReadOnlyDictionary<int, string> Names)
{
    public static FightExtras Empty { get; } = new(Array.Empty<SkillUse>(), Array.Empty<BuffEvent>(), Array.Empty<StaggerEvent>(), new Dictionary<int, string>());

    public bool IsEmpty => Uses.Count == 0 && Buffs.Count == 0 && Staggers.Count == 0;

    /// <summary>The same data under other ids (a stored fight is shown under the ids of the session that shows it).</summary>
    public FightExtras Remap(Func<int, int> map) => new(
        Uses.Select(u => u with { Actor = map(u.Actor) }).ToList(),
        Buffs.Select(b => b with { Target = map(b.Target), Caster = map(b.Caster) }).ToList(),
        Staggers.Select(s => s with { Entity = map(s.Entity) }).ToList(),
        Names.ToDictionary(kv => map(kv.Key), kv => kv.Value));
}
