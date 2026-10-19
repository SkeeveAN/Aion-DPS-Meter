using System.Text.Json;
using System.Text.Json.Serialization;
using AionDPS.Combat;
using AionDPS.Update;

namespace AionDPS.Aion2;

/// <summary>What the player says about the recording: the mode of the calculator and what it assumed (the rates it was set to, the expert settings).</summary>
public sealed record StatRecordingSetup(string Mode, string Side, double SideRate, double CritRate, double AttackRatioBuff, double AmpBuff, double CritAmpBuff, double SmiteBuff, double BossResistance, double CritBase, double SmiteBonus, double CritResist = 0);

/// <summary>Who recorded, without a name or a legion: class, level, server and faction.</summary>
public sealed record StatRecordingCharacter(int? ClassId, int? Level, int? ServerId, string Faction);

/// <summary>A stat that changed while recording (a buff, a potion, a swapped piece): when (seconds from the start), which stat, the new value.</summary>
public readonly record struct StatChange(double At, int Id, int Value);

/// <summary>
/// A recording of the stat calculator: for a while the meter writes down the character's whole stat table (every value of the stat frame, where the game says what each stat adds up
/// to), what changed in it, and the hits of the fights meanwhile - how they landed, what they did, against which monster, with which skill and under which effects. The player sends it
/// to learn, together with other players' recordings, how the stats of this game really add up (the calculator's formula is a guess until then). It holds no name, no legion and no
/// IP address; the one identifier is a random value this installation made for itself.
/// </summary>
public static class StatRecording
{
    public const int FormatVersion = 1;

    /// <summary>A recording stops by itself after this long, so nobody leaves one running by mistake.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(10);

    private const int MaxSkills = 150;
    private const int MaxTargets = 30;
    private const int MaxEvents = 3000;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The kinds of data a recording holds, as keys of the language file (shown before sending), and what it does not hold.</summary>
    public static readonly string[] Contents = { "StatCalc.Send.Item.Stats", "StatCalc.Send.Item.Hits", "StatCalc.Send.Item.Targets", "StatCalc.Send.Item.Effects", "StatCalc.Send.Item.Character", "StatCalc.Send.Item.Id" };

    public static readonly string[] NotContained = { "StatCalc.Send.Not.Names", "StatCalc.Send.Not.Ip", "StatCalc.Send.Not.Chat" };

    /// <summary>The faction of a server id of the game (1xxx Elyos, 2xxx Asmodian), "Unknown" when there is none.</summary>
    public static string FactionOf(int? serverId) => (serverId / 1000) switch { 1 => "Elyos", 2 => "Asmodian", _ => "Unknown" };

    /// <summary>One monster the player hit: the monster's id and name (a player is never listed), how often and for how much.</summary>
    public sealed record TargetEntry(int? NpcId, string? Name, int Hits, long Damage, double FirstAt, double LastAt);

    public sealed record SkillEntry(int SkillId, string? Name, int Hits, long Damage, int Crits, int Backs, int Fronts, int Perfects, int Doubles, int Parries, int Multis);

    /// <summary>The player's own hits in the recorded time, as the document holds them. A hit on a player (PvP) or an estimated one is left out: only the figures the game gave for a monster tell how damage adds up.</summary>
    public static (object Totals, List<SkillEntry> Skills, List<TargetEntry> Targets) Summarize(IReadOnlyList<DamageEvent> mine, DateTime start, Func<int, int?> npcIdOf, Func<int, string?> monsterName)
    {
        var hits = mine.Where(e => !e.IsHeal && e.Amount > 0 && !e.Estimated).ToList();
        var direct = hits.Where(e => !e.IsTick).ToList();
        int Count(Func<DamageEvent, bool> test) => direct.Count(test);
        bool Has(DamageEvent e, HitKinds kind) => e.Kinds is { } k && (k & kind) != 0;

        var totals = new
        {
            hits = direct.Count,
            ticks = hits.Count - direct.Count,
            damage = hits.Sum(e => e.Amount),
            crits = Count(e => e.IsCritical),
            backs = Count(e => Has(e, HitKinds.Back)),
            fronts = Count(e => Has(e, HitKinds.Front)),
            perfects = Count(e => Has(e, HitKinds.Perfect)),
            doubles = Count(e => Has(e, HitKinds.Double)),
            parries = Count(e => Has(e, HitKinds.Parry)),
            multis = Count(e => e.HitCount > 1),
            hitKindsKnown = Count(e => e.Kinds is not null),
        };

        var skills = direct.GroupBy(e => e.SkillId)
            .Select(g => new SkillEntry(g.Key, g.Select(e => e.Skill).FirstOrDefault(n => n is not null), g.Count(), g.Sum(e => e.Amount), g.Count(e => e.IsCritical),
                g.Count(e => Has(e, HitKinds.Back)), g.Count(e => Has(e, HitKinds.Front)), g.Count(e => Has(e, HitKinds.Perfect)), g.Count(e => Has(e, HitKinds.Double)),
                g.Count(e => Has(e, HitKinds.Parry)), g.Count(e => e.HitCount > 1)))
            .OrderByDescending(s => s.Damage).Take(MaxSkills).ToList();

        var targets = new List<TargetEntry>();
        foreach (var g in hits.GroupBy(e => e.TargetObjectId).OrderByDescending(g => g.Sum(e => e.Amount)))
        {
            string? name = monsterName(g.Key);
            int? npc = npcIdOf(g.Key);
            if (name is null && npc is null)
            {
                continue; // not a monster: a player, whose name has no place here
            }

            targets.Add(new TargetEntry(npc, name, g.Count(), g.Sum(e => e.Amount), Math.Round((g.Min(e => e.Timestamp) - start).TotalSeconds, 1), Math.Round((g.Max(e => e.Timestamp) - start).TotalSeconds, 1)));
            if (targets.Count >= MaxTargets)
            {
                break;
            }
        }

        return (totals, skills, targets);
    }

    /// <summary>Builds the document of a recording.</summary>
    public static string Build(string anonymousId, DateTime start, DateTime end, StatRecordingSetup setup, StatRecordingCharacter character,
        IReadOnlyDictionary<int, int> statsStart, IReadOnlyDictionary<int, int> statsEnd, IReadOnlyList<StatChange> changes,
        IReadOnlyList<DamageEvent> mine, FightExtras? extras, int localId, Func<int, int?> npcIdOf, Func<int, string?> monsterName)
    {
        var (totals, skills, targets) = Summarize(mine, start, npcIdOf, monsterName);
        double Sec(DateTime t) => Math.Round((t - start).TotalSeconds, 1);

        var effects = (extras?.Buffs ?? Array.Empty<BuffEvent>()).Where(b => b.Target == localId && b.At <= end) // effects put on before the start are in too (a negative time): they were on the player when it began
            .Take(MaxEvents).Select(b => new { at = Sec(b.At), kind = b.Kind.ToString().ToLowerInvariant(), skillId = b.SkillId, durationMs = b.DurationMs, own = b.Caster == localId }).ToList();
        var uses = (extras?.Uses ?? Array.Empty<SkillUse>()).Where(u => u.Actor == localId && u.At >= start && u.At <= end)
            .Take(MaxEvents).Select(u => new { at = Sec(u.At), skillId = u.SkillId }).ToList();

        var document = new
        {
            kind = "stat-recording",
            formatVersion = FormatVersion,
            clientVersion = AppVersion.Text,
            anonymousId,
            mode = setup.Mode,
            startedAt = start.ToUniversalTime().ToString("O"),
            endedAt = end.ToUniversalTime().ToString("O"),
            durationSeconds = Math.Max(1, (int)Math.Round((end - start).TotalSeconds)),
            character = new { classId = character.ClassId, level = character.Level, serverId = character.ServerId, faction = character.Faction },
            calculator = setup,
            statsStart = statsStart.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value),
            statsEnd = statsEnd.OrderBy(k => k.Key).ToDictionary(k => k.Key.ToString(), k => k.Value),
            statChanges = changes.Take(5000).Select(c => new { at = Math.Round(c.At, 1), id = c.Id, value = c.Value }).ToList(),
            hits = totals,
            skills,
            targets,
            effects,
            uses,
        };
        return JsonSerializer.Serialize(document, Json);
    }
}

/// <summary>Writes down what changes in the character's stat table while a recording runs.</summary>
public sealed class StatRecorder
{
    private Dictionary<int, int> _last = new();
    private readonly List<StatChange> _changes = new();

    public bool Running { get; private set; }

    public DateTime Started { get; private set; }

    public IReadOnlyDictionary<int, int> StartStats { get; private set; } = new Dictionary<int, int>();

    public IReadOnlyList<StatChange> Changes => _changes;

    /// <summary>Starts a recording with the stat table as it is now.</summary>
    public void Start(DateTime now, IReadOnlyDictionary<int, int> stats)
    {
        Running = true;
        Started = now;
        StartStats = new Dictionary<int, int>(stats);
        _last = new Dictionary<int, int>(stats);
        _changes.Clear();
    }

    /// <summary>Looks at the stat table (call it about once a second); true when the recording has reached its longest length.</summary>
    public bool Poll(DateTime now, IReadOnlyDictionary<int, int>? stats)
    {
        if (!Running)
        {
            return false;
        }

        if (stats is not null)
        {
            foreach ((int id, int value) in stats)
            {
                if (!_last.TryGetValue(id, out int before) || before != value)
                {
                    if (_changes.Count < 5000)
                    {
                        _changes.Add(new StatChange((now - Started).TotalSeconds, id, value));
                    }

                    _last[id] = value;
                }
            }
        }

        return now - Started >= StatRecording.MaxLength;
    }

    /// <summary>Ends the recording; the stat table at its end is what <see cref="Poll"/> last saw.</summary>
    public IReadOnlyDictionary<int, int> Stop()
    {
        Running = false;
        return new Dictionary<int, int>(_last);
    }
}
