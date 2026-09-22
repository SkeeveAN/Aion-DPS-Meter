using AionDPS.ChatLog;

namespace AionDPS.Combat.Sources;

/// <summary>
/// Where combat data comes from. The meter's math (LiveAggregator, DpsCalculator, FactionResolver,
/// EngagedTargets) only ever sees <see cref="DamageEvent"/>s and integer object ids; this is the
/// seam that lets MainWindow drive the same UI from Aion's Chat.log today and from an Aion 2
/// packet capture later without knowing which one it is talking to. One instance per session:
/// constructed for a configured game/install, polled once a second from the UI thread, disposed
/// when Settings change.
/// </summary>
public interface ICombatSource : IDisposable
{
    SourceCapabilities Capabilities { get; }

    /// <summary>Object id ↔ display name, plus which id is the local player.</summary>
    IEntityDirectory Entities { get; }

    /// <summary>Zone the local player is in, as far as the source can tell ("" when unknown).</summary>
    string CurrentZone { get; }

    /// <summary>Whether the local player is in an arena, where the opponent may share their faction.</summary>
    bool InArena { get; }

    /// <summary>Begins watching. May be called before the underlying input exists (Chat.log not
    /// yet written, game not yet running) - the source keeps trying on every <see cref="Poll"/>.</summary>
    void Start();

    void Stop();

    /// <summary>Everything that happened since the previous call. Paused time is discarded, not
    /// deferred: the returned batch is empty while paused, but control signals
    /// (<see cref="CommandReceived"/>) still fire so ".resume" can work.</summary>
    CombatBatch Poll(bool paused);

    /// <summary>(actor name, skill) - "You" for the local player on the Chat.log source.</summary>
    event Action<string, string>? SkillUsed;

    /// <summary>(sender name or null, command, argument) from an in-game chat line.</summary>
    event Action<string?, string, string>? CommandReceived;

    event Action<PersonalStatKind, long>? PersonalStatChanged;
    event Action<string>? PlayerLoggedIn;
    event Action<LootEvent>? LootAcquired;
    event Action<BuffCastEvent>? BuffCast;

    /// <summary>Human-readable state of the input (waiting for the game, connected, protocol not
    /// calibrated, …) for the status bar. Fires on the polling thread.</summary>
    event Action<SourceStatus>? StatusChanged;
}

/// <summary>Object id ↔ name for one source. Ids are only meaningful within the source that issued them.</summary>
public interface IEntityDirectory
{
    string? NameFor(int id);

    int GetOrAssignId(string name);

    /// <summary>The local player's id - what Chat.log calls "You".</summary>
    int LocalPlayerId { get; }

    bool IsLocalPlayer(int id);
}

/// <summary>What a source can deliver, so the UI hides panels a source can never fill (the Loot
/// tab for a source that sees no loot, the AP footer for one without personal stats).</summary>
[Flags]
public enum SourceCapabilities
{
    None = 0,
    Loot = 1 << 0,
    ChatCommands = 1 << 1,
    PersonalStats = 1 << 2,
    Buffs = 1 << 3,
    /// <summary>Dodge/parry/block/resist events (<see cref="AvoidEvent"/>).</summary>
    Defense = 1 << 4,
    /// <summary>Kill/death events (<see cref="KillEvent"/>).</summary>
    Kills = 1 << 5,
    /// <summary>Ids are the game's own object ids, not synthetic per-name ones - two same-named
    /// entities are told apart.</summary>
    ExactIds = 1 << 6,
    /// <summary>The whole history can be re-read on request (Chat.log's "Reload from disk").</summary>
    Reparse = 1 << 7,
}

public enum SourceState
{
    Idle,
    Waiting,
    Connected,
    Error,
}

public readonly record struct SourceStatus(SourceState State, string Message);

/// <summary>One poll's worth of new events. Avoids and kills are separate lists (not DamageEvents
/// with a zero amount) so the DPS math never has to filter them out.</summary>
public readonly record struct CombatBatch(
    IReadOnlyList<DamageEvent> Damage,
    IReadOnlyList<AvoidEvent> Avoids,
    IReadOnlyList<KillEvent> Kills)
{
    public static CombatBatch Empty { get; } = new(Array.Empty<DamageEvent>(), Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>());

    public static CombatBatch DamageOnly(IReadOnlyList<DamageEvent> damage) =>
        new(damage, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>());

    public bool IsEmpty => Damage.Count == 0 && Avoids.Count == 0 && Kills.Count == 0;
}

public enum AvoidKind
{
    Dodge,
    Parry,
    Block,
    Resist,
}

/// <summary>An attack that landed no damage because the target avoided it - the defensive
/// counterpart of <see cref="DamageEvent"/>. Source is the attacker, Target the one who avoided.</summary>
public readonly record struct AvoidEvent(
    DateTime Timestamp,
    int SourceObjectId,
    int TargetObjectId,
    AvoidKind Kind,
    string? Skill = null);

/// <summary>Someone died. <see cref="KillerObjectId"/> is null when the source only saw the death,
/// not who caused it; <see cref="VictimIsPlayer"/> is what separates PvP kills from mob kills.</summary>
public readonly record struct KillEvent(
    DateTime Timestamp,
    int? KillerObjectId,
    int VictimObjectId,
    bool VictimIsPlayer);
