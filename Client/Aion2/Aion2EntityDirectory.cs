using AionDPS.Combat.Sources;

namespace AionDPS.Aion2;

/// <summary>One equipped item as the character record lists it: its position and its id.</summary>
public sealed record Aion2EquippedItem(int SlotIndex, int ItemId, int Enchant = 0);

/// <summary>One learned skill: total level (what the game shows) and the trained base level; the
/// difference is a bonus from gear or other sources.</summary>
public sealed record Aion2SkillEntry(int SkillId, int Level, int BaseLevel);

/// <summary>What the "player appeared" frame says about another player: class and faction (decoded
/// from its class code) and the visible equipment (no enchant levels in that list).</summary>
public sealed record Aion2SeenProfile(int? ClassId, int? Faction, IReadOnlyList<Aion2EquippedItem> Gear);

/// <summary>The activated node ids of one Daevanion board (the start node included).</summary>
public sealed record Aion2DaevanionBoard(int BoardId, IReadOnlyList<int> NodeIds);

/// <summary>The local player's character record (opcode 0x3336), as of when the server last sent it
/// - at login and on every zone change.</summary>
public sealed record Aion2CharacterInfo(int CombatId, string Name, int ClassCode, int Level, IReadOnlyList<Aion2EquippedItem> Equipment, DateTime ReceivedAt, bool Restored = false, int ServerId = 0);

/// <summary>Aion 2 frames carry the game's own object ids, so this maps those to names as nickname
/// frames reveal them. The local player is whichever id the session frame names. Names that never
/// came with a game id (hand-entered characters) get synthetic negative ids so they can never
/// collide with a real object id.</summary>
/// <summary>What the directory has learned about the objects around: enough to carry a session across a
/// client restart (the game keeps running, so its object ids stay valid; only the meter forgets who is
/// who - the "appeared" frames of players already in view are not sent again).</summary>
public sealed record Aion2DirectorySnapshot(
    Dictionary<int, string> Names,
    Dictionary<int, string> Classes,
    Dictionary<int, string> Guilds,
    Dictionary<int, int> BossNpcs,
    int LocalPlayerId);

public sealed class Aion2EntityDirectory : IEntityDirectory
{
    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<string, int> _ids = new();
    // Per object: how often each class's skills were seen with it as the actor. A received buff
    // (a Chanter's Mantra on a Gladiator) arrives with the recipient as actor, so the LAST class
    // seen is wrong - the most frequent one is right.
    private readonly Dictionary<int, Dictionary<string, int>> _classVotes = new();
    private readonly object _gate = new();

    // Name -> how many roster frames listed it. The party's own members are listed in every update;
    // a stray look-alike (a member who left, a byte run that happens to fit) is listed once.
    private readonly Dictionary<string, int> _roster = new(StringComparer.Ordinal);
    private readonly HashSet<string> _notPlayers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _guilds = new();
    private int _explicitLocalId = -1;
    private Aion2CharacterInfo? _character;
    private string? _configuredLocalName;

    /// <summary>
    /// The local player: a session frame's id when one is decoded, else the object whose name is the
    /// configured character name (<see cref="SetConfiguredLocalName"/>), else the one inferred from
    /// the stream (see <see cref="InferLocalPlayer"/>). -1 while none is known.
    /// </summary>
    public int LocalPlayerId
    {
        get
        {
            if (_explicitLocalId >= 0)
            {
                return _explicitLocalId;
            }

            lock (_gate)
            {
                if (_configuredLocalName is not null && _ids.TryGetValue(_configuredLocalName, out int byName))
                {
                    return byName;
                }
            }

            return InferLocalPlayer() ?? -1;
        }

        internal set => _explicitLocalId = value;
    }

    /// <summary>The user's own Aion 2 character name from Settings (empty = none).</summary>
    public void SetConfiguredLocalName(string? name)
    {
        lock (_gate)
        {
            _configuredLocalName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
    }

    /// <summary>The local player's name when the stream alone reveals it (the party roster's leftover
    /// name, see <see cref="LocalRosterName"/>) - what the meter then remembers in Settings so the
    /// next solo session needs no party to know it.</summary>
    public string? LearnedLocalName
    {
        get
        {
            lock (_gate)
            {
                return InferLocalPlayer() is not null ? LocalRosterName() : null;
            }
        }
    }

    /// <summary>The local player's own record. It is only ever sent for yourself (everyone else gets
    /// the "appeared" frame), so it also settles who the local player is.</summary>
    public void SetLocalCharacter(Aion2CharacterInfo info)
    {
        lock (_gate)
        {
            _character = info;
            _explicitLocalId = info.CombatId;
            _names[info.CombatId] = info.Name;
            _ids[info.Name] = info.CombatId;
        }

        CharacterChanged?.Invoke(info);
    }

    public Aion2CharacterInfo? LocalCharacter
    {
        get
        {
            lock (_gate)
            {
                return _character;
            }
        }
    }

    public event Action<Aion2CharacterInfo>? CharacterChanged;

    /// <summary>Loads what an earlier session saved (see <see cref="Aion2CharacterStore"/>). The combat
    /// id of a saved record is meaningless in this session, so it is not made the local player; the
    /// record just fills the Character view and the profile upload until the game sends the real one.</summary>
    public void RestoreFrom(Aion2SavedCharacter saved)
    {
        lock (_gate)
        {
            if (_character is not null)
            {
                return; // fresh data already arrived
            }

            var equipment = saved.Equipment.Select(i => new Aion2EquippedItem(i.Slot, i.ItemId, i.Enchant)).ToList();
            _character = new Aion2CharacterInfo(-1, saved.Name, saved.ClassCode, saved.Level, equipment, saved.SavedAt, Restored: true, ServerId: saved.ServerId);
            _fullEquipment = equipment;
            _skills = saved.Skills.Select(s => new Aion2SkillEntry(s.Id, s.Level, s.BaseLevel)).ToList();
            _daevanion = saved.Daevanion.Select(b => new Aion2DaevanionBoard(b.Board, b.Nodes)).ToList();
        }
    }

    /// <summary>The local player's data as it should be kept on disk, or null before any is known.</summary>
    public Aion2SavedCharacter? ToSaved()
    {
        lock (_gate)
        {
            if (_character is not { Restored: false } c)
            {
                return null;
            }

            return new Aion2SavedCharacter
            {
                Name = c.Name,
                ClassCode = c.ClassCode,
                Level = c.Level,
                ServerId = c.ServerId,
                SavedAt = DateTime.Now,
                Equipment = (_fullEquipment ?? c.Equipment).Select(i => new Aion2SavedCharacter.SavedItem(i.SlotIndex, i.ItemId, i.Enchant)).ToList(),
                Skills = (_skills ?? Array.Empty<Aion2SkillEntry>()).Select(s => new Aion2SavedCharacter.SavedSkill(s.SkillId, s.Level, s.BaseLevel)).ToList(),
                Daevanion = (_daevanion ?? Array.Empty<Aion2DaevanionBoard>()).Select(b => new Aion2SavedCharacter.SavedBoard(b.BoardId, b.NodeIds.ToList())).ToList(),
            };
        }
    }

    private IReadOnlyList<Aion2EquippedItem>? _fullEquipment;
    private IReadOnlyList<Aion2SkillEntry>? _skills;

    /// <summary>Every slot with its enchant level (the login equipment record); falls back to the
    /// 11 visible slots of the character record until that arrives.</summary>
    public IReadOnlyList<Aion2EquippedItem> LocalEquipment
    {
        get
        {
            lock (_gate)
            {
                return _fullEquipment ?? _character?.Equipment ?? Array.Empty<Aion2EquippedItem>();
            }
        }
    }

    public IReadOnlyList<Aion2SkillEntry> LocalSkills
    {
        get
        {
            lock (_gate)
            {
                return _skills ?? Array.Empty<Aion2SkillEntry>();
            }
        }
    }

    public void SetLocalEquipment(IReadOnlyList<Aion2EquippedItem> equipment)
    {
        lock (_gate)
        {
            _fullEquipment = equipment;
        }

        NotifyCharacterChanged();
    }

    private IReadOnlyList<Aion2DaevanionBoard>? _daevanion;

    public IReadOnlyList<Aion2DaevanionBoard> LocalDaevanion
    {
        get
        {
            lock (_gate)
            {
                return _daevanion ?? Array.Empty<Aion2DaevanionBoard>();
            }
        }
    }

    public void SetLocalDaevanion(IReadOnlyList<Aion2DaevanionBoard> boards)
    {
        lock (_gate)
        {
            _daevanion = boards;
        }

        NotifyCharacterChanged();
    }

    public void SetLocalSkills(IReadOnlyList<Aion2SkillEntry> skills)
    {
        lock (_gate)
        {
            _skills = skills;
        }

        NotifyCharacterChanged();
    }

    private void NotifyCharacterChanged()
    {
        Aion2CharacterInfo? current = LocalCharacter;
        if (current is not null)
        {
            CharacterChanged?.Invoke(current);
        }
    }

    private readonly Dictionary<int, Aion2SeenProfile> _seen = new();

    // Entity id -> NPC id, kept only for monsters that are bosses (see Aion2BossCatalog).
    private readonly Dictionary<int, int> _bossNpcs = new();

    /// <summary>Remembers that the monster with this entity id is the boss with this NPC id. Called
    /// when the game announces a monster; ids that are no boss are ignored.</summary>
    public void RegisterNpc(int entityId, int npcId)
    {
        if (Protocol.Aion2BossCatalog.Find(npcId) is null)
        {
            return;
        }

        lock (_gate)
        {
            _bossNpcs[entityId] = npcId;
        }
    }

    /// <summary>Every monster recognised as a boss so far: entity id and NPC id.</summary>
    public IReadOnlyList<(int EntityId, int NpcId)> KnownBosses()
    {
        lock (_gate)
        {
            return _bossNpcs.Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }

    /// <summary>The boss NPC id of an entity, or null when it is no known boss.</summary>
    public int? BossNpcIdOf(int entityId)
    {
        lock (_gate)
        {
            return _bossNpcs.TryGetValue(entityId, out int npcId) ? npcId : null;
        }
    }

    public void SetSeenProfile(int id, Aion2SeenProfile profile)
    {
        lock (_gate)
        {
            _seen[id] = profile;
        }
    }

    /// <summary>"Elyos" as the network states it: the low bits of the class code are 2 for Elyos
    /// (checked against two Elyos characters and the Elyos legion Akatsuki's member list). The other
    /// value seen (1) also occurs inside that Elyos legion, so it is NOT shown as Asmodian - its
    /// meaning is unknown. From the own character's record or a seen appearance; null otherwise.</summary>
    public string? FactionOf(int id)
    {
        int? bit = IsLocalPlayer(id) && LocalCharacter is { } own && own.ClassCode % 4 is 1 or 2
            ? own.ClassCode % 4
            : SeenProfileOf(id)?.Faction;
        return bit == 2 ? "Elyos" : null;
    }

    public Aion2DirectorySnapshot Snapshot()
    {
        lock (_gate)
        {
            return new Aion2DirectorySnapshot(
                new Dictionary<int, string>(_names),
                _classVotes.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value.MaxBy(v => v.Value).Key),
                new Dictionary<int, string>(_guilds),
                new Dictionary<int, int>(_bossNpcs),
                _explicitLocalId);
        }
    }

    /// <summary>Takes over a snapshot from before a restart. Anything the live stream already knows wins;
    /// restored classes count as strong evidence (they were voted on for the whole session).</summary>
    public void RestoreFrom(Aion2DirectorySnapshot snapshot)
    {
        lock (_gate)
        {
            foreach ((int id, string name) in snapshot.Names)
            {
                if (!_names.ContainsKey(id) && !_ids.ContainsKey(name))
                {
                    _names[id] = name;
                    _ids[name] = id;
                }
            }

            foreach ((int id, string className) in snapshot.Classes)
            {
                if (!_classVotes.ContainsKey(id))
                {
                    _classVotes[id] = new Dictionary<string, int> { [className] = 50 };
                }
            }

            foreach ((int id, string guild) in snapshot.Guilds)
            {
                _guilds.TryAdd(id, guild);
            }

            foreach ((int entityId, int npcId) in snapshot.BossNpcs)
            {
                _bossNpcs.TryAdd(entityId, npcId);
            }

            if (_explicitLocalId < 0 && snapshot.LocalPlayerId >= 0)
            {
                _explicitLocalId = snapshot.LocalPlayerId;
            }
        }
    }

    /// <summary>The ids of every player whose equipment has been seen so far.</summary>
    public IReadOnlyList<int> SeenProfileIds()
    {
        lock (_gate)
        {
            return _seen.Keys.ToList();
        }
    }

    public Aion2SeenProfile? SeenProfileOf(int id)
    {
        lock (_gate)
        {
            return _seen.GetValueOrDefault(id);
        }
    }

    public void SetGuild(int id, string guild)
    {
        lock (_gate)
        {
            _guilds[id] = guild;
        }
    }

    public string? GuildOf(int id)
    {
        lock (_gate)
        {
            return _guilds.GetValueOrDefault(id);
        }
    }

    /// <summary>Adds a name from the party roster frame. The roster lists the local player too, who
    /// is the one member whose own nickname frame never arrives (everyone else "appears" to you).</summary>
    public void NoteRosterName(string name)
    {
        lock (_gate)
        {
            _roster[name] = _roster.GetValueOrDefault(name) + 1;
        }
    }

    /// <summary>A name the roster shows that is not a party member - the guild name, which every
    /// member's nickname frame repeats after its own name.</summary>
    public void NoteNonPlayerName(string name)
    {
        lock (_gate)
        {
            _notPlayers.Add(name);
        }
    }

    /// <summary>
    /// The local player, worked out from the stream: the object seen casting class skills that never
    /// got a nickname frame. Only claimed when it is unambiguous - one such object, or one clearly
    /// dominant by skill count - otherwise null and nobody is called "you".
    /// </summary>
    public int? InferLocalPlayer()
    {
        lock (_gate)
        {
            var unnamed = _classVotes.Where(kv => !_names.ContainsKey(kv.Key))
                .Select(kv => (Id: kv.Key, Votes: kv.Value.Values.Sum()))
                .OrderByDescending(x => x.Votes)
                .ToList();
            if (unnamed.Count == 0 || (unnamed.Count > 1 && unnamed[0].Votes < 3 * unnamed[1].Votes))
            {
                return null;
            }

            return unnamed[0].Id;
        }
    }

    /// <summary>Diagnostic line for the replay tool.</summary>
    public string Describe()
    {
        lock (_gate)
        {
            return $"named [{string.Join(", ", _names.Values)}] roster [{string.Join(", ", _roster.Select(kv => kv.Key + "x" + kv.Value))}] notPlayers [{string.Join(", ", _notPlayers)}] leftover [{LocalRosterName()}]";
        }
    }

    // The roster name nobody else claimed: the local player's, when exactly one is left over.
    private string? LocalRosterName()
    {
        var left = _roster.Where(kv => !_ids.ContainsKey(kv.Key) && !_notPlayers.Contains(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .ToList();
        if (left.Count == 0 || (left.Count > 1 && left[0].Value < 3 * left[1].Value))
        {
            return null;
        }

        return left[0].Key;
    }

    /// <summary>The registered name; else, for an object seen casting class skills, "Player #id" -
    /// the combat frames carry only ids, and a player's name arrives separately (and sometimes
    /// late), so this keeps players apart until it does. The class is shown by the row's icon and
    /// class column, so it is deliberately not repeated in the name.</summary>
    public string? NameFor(int id)
    {
        string? registered;
        lock (_gate)
        {
            registered = _names.GetValueOrDefault(id);
            if (registered is null && _bossNpcs.TryGetValue(id, out int npcId) && Protocol.Aion2BossCatalog.Find(npcId) is { } boss)
            {
                return boss.Name;
            }

            if (registered is null && InferLocalPlayer() == id)
            {
                registered = LocalRosterName();
            }
        }

        return registered ?? (ClassOf(id) is not null ? $"Player #{id}" : null);
    }

    /// <summary>The class an object has most often been seen casting, or null.</summary>
    public string? ClassOf(int id)
    {
        lock (_gate)
        {
            return _classVotes.TryGetValue(id, out var votes) ? votes.MaxBy(v => v.Value).Key : null;
        }
    }

    /// <summary>Remembers which class an object plays, learned from the class prefix of its skills.</summary>
    public void NoteClass(int id, string className)
    {
        lock (_gate)
        {
            if (!_classVotes.TryGetValue(id, out var votes))
            {
                votes = new Dictionary<string, int>();
                _classVotes[id] = votes;
            }

            votes[className] = votes.GetValueOrDefault(className) + 1;
        }
    }

    /// <summary>True once the object has been seen using a class skill - i.e. it is a player.</summary>
    public bool IsKnownPlayer(int id)
    {
        lock (_gate)
        {
            return _classVotes.ContainsKey(id);
        }
    }

    public int GetOrAssignId(string name)
    {
        if (_ids.TryGetValue(name, out int id))
        {
            return id;
        }

        id = -(_ids.Count + 2);
        Register(id, name);
        return id;
    }

    public bool IsLocalPlayer(int id) => LocalPlayerId is >= 0 and var local && id == local;

    public void Register(int id, string name)
    {
        lock (_gate)
        {
            _names[id] = name;
            _ids[name] = id;
        }
    }
}
