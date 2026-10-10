using AionDPS.Combat.Sources;

using AionDPS.Aion2.Protocol;

namespace AionDPS.Aion2;

/// <summary>One equipped item as the character record lists it: its position and its id, the enchant level and,
/// where the frame carries them, the mana stones set into it and the rolled stats of the piece (null = not read).</summary>
public sealed record Aion2EquippedItem(int SlotIndex, int ItemId, int Enchant = 0, IReadOnlyList<Aion2Stone>? Stones = null, IReadOnlyList<Aion2RolledStat>? Stats = null, int GodstoneId = 0, IReadOnlyList<Aion2SkillBonus>? SkillBonuses = null);

/// <summary>A mana stone slot of an item: the stat the stone gives (the game's stat id, 0 = empty slot) and its tier
/// (1 white, 2 green, 3 blue; the amount the stone adds is not in the frame).</summary>
public sealed record Aion2Stone(int StatId, int Tier);

/// <summary>A skill level bonus on an item ("Abwärtsschlag St. +1").</summary>
public sealed record Aion2SkillBonus(int SkillId, int Level);

/// <summary>What an equipment entry carries behind the enchant level (see Aion2FrameDecoder.TryReadItemDetails).</summary>
public sealed record Aion2ItemDetails(IReadOnlyList<Aion2Stone> Stones, IReadOnlyList<Aion2RolledStat>? Stats, int GodstoneId, IReadOnlyList<Aion2SkillBonus>? SkillBonuses);

/// <summary>A stat that was rolled on the item itself (stat id and its value, in the game's own units).</summary>
public sealed record Aion2RolledStat(int StatId, long Value);

/// <summary>One learned skill: total level (what the game shows) and the trained base level; the
/// difference is a bonus from gear or other sources.</summary>
public sealed record Aion2SkillEntry(int SkillId, int Level, int BaseLevel, bool Stigma = false, bool Equipped = false);

/// <summary>What the "player appeared" frame says about another player: class and the class bits (decoded
/// from its class code; the low two bits are NOT a faction - Elyos characters carry 1 and 2) and the visible
/// equipment (no enchant levels in that list).</summary>
public sealed record Aion2SeenProfile(int? ClassId, int? ClassBits, IReadOnlyList<Aion2EquippedItem> Gear);

/// <summary>Another player's character window as the server sent it (opcode 0x5036): no object id, only the
/// name. <see cref="ClassCode"/> is <c>4 * class id + class bits</c> (no faction); the gear carries enchant levels.</summary>
public sealed record Aion2InspectedPlayer(
    string Name,
    int ClassCode,
    int Level,
    int GearScore,
    string? Guild,
    IReadOnlyList<Aion2EquippedItem> Gear,
    DateTime ReceivedAt,
    IReadOnlyList<Aion2TitleSlot>? Titles = null,
    IReadOnlyList<Aion2Pet>? Pets = null,
    IReadOnlyList<Aion2BoardCount>? BoardCounts = null);

/// <summary>The wing item id (30xxxxxx) and the wing skin id (304xxxxx, 0 = no skin) of the local player.</summary>
public sealed record Aion2Wing(int WingId, int SkinId);

/// <summary>Constants of the stat frame (opcode 0x4936 = 18742, own character only). Verified 2026-10-10 on Aahz: the
/// ids 1..6 are the base attributes (1 Might, 2 Agility, 3 Intelligence, 4 Vitality, 5 Precision, 6 Willpower;
/// Intelligence is not sent while 0) and 7..17 the Lord values (7 Justice, 8 Freedom, 9 Illusion, 10 Life, 11 Time,
/// 12 always 0, 13 Destruction, 14 Death, 15 Wisdom, 16 Destiny, 17 Space); all matched the website's character page.</summary>
public static class Aion2Attributes
{
    public const int FirstId = 1;
    public const int LastId = 17;
}

/// <summary>A worn title: the slot (1..3) and the game's title id (see Table/Title.dat).</summary>
public sealed record Aion2TitleSlot(int Slot, int TitleId);

/// <summary>One pet circle of the growth overview: the species (2 Cognia .. 6 Specia), its level and the quality of
/// each effect slot (0 empty, 1 white, 2 green, 3 blue, 4 gold, 5 orange).</summary>
public sealed record Aion2Pet(int SpeciesId, int Level, IReadOnlyList<int> Kinds);

/// <summary>How many nodes of a Daevanion board another player has activated (the start node included).</summary>
public sealed record Aion2BoardCount(int BoardId, int Count);

/// <summary>The activated node ids of one Daevanion board (the start node included).</summary>
public sealed record Aion2DaevanionBoard(int BoardId, IReadOnlyList<int> NodeIds);

/// <summary>One analysed effect of a species knowledge: the page (1..3), the slot on it, the game's stat id and
/// the value (percent stats are in hundredths).</summary>
public sealed record Aion2SpeciesEffect(int Page, int Slot, int StatId, long Value, int Kind = 0);

/// <summary>One species knowledge of the pet window: id 2 Cognia, 3 Fera, 4 Natura, 5 Varia, 6 Specia.</summary>
public sealed record Aion2SpeciesKnowledge(int SpeciesId, int Level, long Progress, IReadOnlyList<Aion2SpeciesEffect> Effects);

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

    /// <summary>The local player's name as its own character record states it - what the meter then
    /// remembers in Settings so the next session knows it from the start. It used to be the party
    /// roster's leftover name (the one no visible player carries), which, once the roster was read on
    /// every server, could be a team mate not named yet: that name was then saved as one's own and the
    /// own row showed under a team mate's name.</summary>
    public string? LearnedLocalName
    {
        get
        {
            lock (_gate)
            {
                return _character is { Restored: false } own && own.Name.Length > 0 ? own.Name : null;
            }
        }
    }

    /// <summary>The local player's own record. It is only ever sent for yourself (everyone else gets
    /// the "appeared" frame), so it also settles who the local player is.</summary>
    public void SetLocalCharacter(Aion2CharacterInfo info)
    {
        int? renamedFrom = null;
        lock (_gate)
        {
            // Another character logged in (a twink): the lists of the previous one must not leak into
            // its profile, and the skill list is merged, so it would never go away by itself. Lists
            // that arrived within moments before this record already belong to the new character.
            if (_character is { } previous && previous.Name != info.Name && DateTime.UtcNow - _listsAt > TimeSpan.FromSeconds(3))
            {
                _skills = null;
                _fullEquipment = null;
                _daevanion = null;
                _species = null;
                _petStates = null;
                _titles = null;
                _attributes = null;
                _wing = null;
                _activePet = null;
                _stigmas.Clear();
                _bar.Clear();
            }

            int previousId = _explicitLocalId;
            string? previousName = _character?.Name;
            _character = info;
            _explicitLocalId = info.CombatId;
            if (previousId >= 0 && previousId != info.CombatId && previousName == info.Name)
            {
                renamedFrom = previousId;
            }

            _names[info.CombatId] = info.Name;
            _ids[info.Name] = info.CombatId;
        }

        if (renamedFrom is int oldId)
        {
            ForgetPlace();
            LocalIdChanged?.Invoke(oldId, info.CombatId);
        }

        CharacterChanged?.Invoke(info);
    }

    /// <summary>The own character was announced again under another combat id (a map change); the old id and the new one are the same player.</summary>
    public event Action<int, int>? LocalIdChanged;

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

            var equipment = saved.Equipment.Select(i => new Aion2EquippedItem(i.Slot, i.ItemId, i.Enchant, i.Stones, i.Stats, i.GodstoneId, i.SkillBonuses)).ToList();
            _character = new Aion2CharacterInfo(-1, saved.Name, saved.ClassCode, saved.Level, equipment, saved.SavedAt, Restored: true, ServerId: saved.ServerId);
            _fullEquipment = equipment;
            _skills = saved.Skills.Select(s => new Aion2SkillEntry(s.Id, s.Level, s.BaseLevel, s.Stigma, s.Equipped)).ToList();
            _daevanion = saved.Daevanion.Select(b => new Aion2DaevanionBoard(b.Board, b.Nodes)).ToList();
            _species = saved.Species.Select(k => new Aion2SpeciesKnowledge(k.Id, k.Level, k.Progress,
                k.Effects.Select(e => new Aion2SpeciesEffect(e.Page, e.Slot, e.Stat, e.Value, e.Kind)).ToList())).ToList();
            _titles = saved.Titles.Select(t => new Aion2TitleSlot(t.Slot, t.TitleId)).ToList();
            _attributes = saved.Attributes is { Count: > 0 } ? new Dictionary<int, int>(saved.Attributes) : null;
            _wing = saved.WingId is int savedWing ? new Aion2Wing(savedWing, saved.WingSkinId ?? 0) : null;
            _activePet = saved.ActivePet;
            _petStates = saved.Pets.Select(p => new Aion2PetState(p.Id, p.Level, p.Progress)).ToList();
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
                Equipment = (_fullEquipment ?? c.Equipment).Select(i => new Aion2SavedCharacter.SavedItem(i.SlotIndex, i.ItemId, i.Enchant, i.Stones, i.Stats, i.GodstoneId, i.SkillBonuses)).ToList(),
                Skills = (_skills ?? Array.Empty<Aion2SkillEntry>()).Select(s => new Aion2SavedCharacter.SavedSkill(s.SkillId, s.Level, s.BaseLevel, s.Stigma, s.Equipped)).ToList(),
                Daevanion = (_daevanion ?? Array.Empty<Aion2DaevanionBoard>()).Select(b => new Aion2SavedCharacter.SavedBoard(b.BoardId, b.NodeIds.ToList())).ToList(),
                Species = (_species ?? Array.Empty<Aion2SpeciesKnowledge>()).Select(k => new Aion2SavedCharacter.SavedSpecies(k.SpeciesId, k.Level, k.Progress,
                    k.Effects.Select(e => new Aion2SavedCharacter.SavedEffect(e.Page, e.Slot, e.StatId, e.Value, e.Kind)).ToList())).ToList(),
                Titles = (_titles ?? Array.Empty<Aion2TitleSlot>()).Select(t => new Aion2SavedCharacter.SavedTitle(t.Slot, t.TitleId)).ToList(),
                Attributes = _attributes is null ? null : new Dictionary<int, int>(_attributes),
                WingId = _wing?.WingId,
                WingSkinId = _wing?.SkinId,
                ActivePet = _activePet,
                Pets = (_petStates ?? Array.Empty<Aion2PetState>()).Select(p => new Aion2SavedCharacter.SavedPet(p.PetId, p.Level, p.Progress)).ToList(),
            };
        }
    }

    private DateTime _listsAt = DateTime.MinValue;
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
            _listsAt = DateTime.UtcNow;
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

    private IReadOnlyList<Aion2PetState>? _petStates;

    /// <summary>Every pet the local player owns with its level and progress: the pet list the game sends at login and map change, kept
    /// up to date by the game's own messages (progress gained, level reached, pet added). Empty until the list arrived.</summary>
    public IReadOnlyList<Aion2PetState> LocalPetStates
    {
        get
        {
            lock (_gate)
            {
                return _petStates ?? Array.Empty<Aion2PetState>();
            }
        }
    }

    /// <summary>The game announced progress for a pet (a soul given: +1, two dropped at once: +2).</summary>
    public void NotePetProgress(int petId, int gained)
    {
        ChangePet(petId, p => p with { Progress = p.Progress + gained });
    }

    /// <summary>A pet reached a new level (its progress starts again at 0).</summary>
    public void NotePetLevel(int petId, int level)
    {
        ChangePet(petId, p => p with { Level = level, Progress = 0 });
    }

    /// <summary>A pet joined the collection.</summary>
    public void NotePetAdded(int petId, int level)
    {
        lock (_gate)
        {
            if (_petStates is not null && _petStates.All(p => p.PetId != petId))
            {
                _petStates = _petStates.Append(new Aion2PetState(petId, level, 0)).ToList();
            }
        }
    }

    private void ChangePet(int petId, Func<Aion2PetState, Aion2PetState> change)
    {
        lock (_gate)
        {
            if (_petStates is null || _petStates.All(p => p.PetId != petId))
            {
                return;
            }

            _petStates = _petStates.Select(p => p.PetId == petId ? change(p) : p).ToList();
        }

        NotifyCharacterChanged();
    }

    public void SetLocalPetStates(IReadOnlyList<Aion2PetState> pets)
    {
        lock (_gate)
        {
            // the log tells where the kept-up-to-date state and the list of the game differ (login, map change)
            if (_petStates is not null)
            {
                foreach (var game in pets)
                {
                    var tracked = _petStates.FirstOrDefault(p => p.PetId == game.PetId);
                    if (tracked is not null && tracked != game)
                    {
                        PetFarmLog.Write($"CHECK {Aion2Pets.PetName(game.PetId, "en") ?? "?"} (pet {game.PetId}): counted L{tracked.Level} {tracked.Progress}, the game's list says L{game.Level} {game.Progress}");
                    }
                }
            }

            _petStates = pets;
            _listsAt = DateTime.UtcNow;
        }

        NotifyCharacterChanged();
    }

    private (float X, float Y, DateTime At)? _position;
    private readonly Queue<(int NpcId, float X, float Y)> _mobPositions = new();

    /// <summary>The last known position of the local player (world units, 100 per metre) and when it was reported; null before any.</summary>
    public (float X, float Y, DateTime At)? LocalPosition
    {
        get
        {
            lock (_gate)
            {
                return _position;
            }
        }
    }

    private readonly Queue<(DateTime At, float X, float Y)> _recentSpawns = new();

    /// <summary>
    /// The best guess of where the player is: the server only reports the player's own position at skills, hits and corrections, but it
    /// announces every monster that comes into range (a radius of roughly 65 m) with its place. The middle of the monsters announced in the
    /// last 8 seconds follows the player while he walks (checked against 12 position reports of a long run: 8 m off in the median, 27 m at
    /// worst). The newer of the two wins: a skill's exact position, or the middle of the latest announcements (at least two).
    /// </summary>
    public (float X, float Y, DateTime At)? BestPosition
    {
        get
        {
            lock (_gate)
            {
                DateTime now = DateTime.UtcNow;
                var recent = _recentSpawns.Where(s => now - s.At <= TimeSpan.FromSeconds(8)).ToList();
                if (recent.Count >= 2 && (_position is null || recent.Max(s => s.At) > _position.Value.At))
                {
                    var xs = recent.Select(s => s.X).OrderBy(v => v).ToList();
                    var ys = recent.Select(s => s.Y).OrderBy(v => v).ToList();
                    return (xs[xs.Count / 2], ys[ys.Count / 2], recent.Max(s => s.At));
                }

                // The game reports the player's own position only for some movements (not when a monster is targeted), so it can stay unknown
                // for a long time (after a relog, or standing still). The monsters around him, announced and not yet gone again, still
                // surround him (within the ~65 m range): their middle beats "unknown" and beats a report that is more than 15 s old.
                if ((_position is null || now - _position.Value.At > TimeSpan.FromSeconds(15)) && _liveMobs.Count >= 3)
                {
                    var xs = _liveMobs.Values.Select(m => m.X).OrderBy(v => v).ToList();
                    var ys = _liveMobs.Values.Select(m => m.Y).OrderBy(v => v).ToList();
                    return (xs[xs.Count / 2], ys[ys.Count / 2], now);
                }

                return _position;
            }
        }
    }

    public void NoteLocalPosition(float x, float y)
    {
        lock (_gate)
        {
            _position = (x, y, DateTime.UtcNow);
        }
    }

    /// <summary>The monsters announced most recently with their place (the last 60), to tell which map the player is on.</summary>
    public void NoteMobPosition(int entityId, int npcId, float x, float y)
    {
        lock (_gate)
        {
            _liveMobs[entityId] = (npcId, x, y);
            _recentSpawns.Enqueue((DateTime.UtcNow, x, y));
            while (_recentSpawns.Count > 0 && DateTime.UtcNow - _recentSpawns.Peek().At > TimeSpan.FromSeconds(30))
            {
                _recentSpawns.Dequeue();
            }
            if (_liveMobs.Count > 3000)
            {
                _liveMobs.Clear(); // nothing is ever that close: the table lost its removals
            }

            _mobPositions.Enqueue((npcId, x, y));
            while (_mobPositions.Count > 60)
            {
                _mobPositions.Dequeue();
            }
        }
    }

    private readonly Dictionary<int, (int NpcId, float X, float Y)> _liveMobs = new();

    /// <summary>The monsters around the player right now: announced when they come into range (with their place) and gone again when the
    /// game says they died or disappeared.</summary>
    public void ForgetLiveMob(int entityId)
    {
        lock (_gate)
        {
            _liveMobs.Remove(entityId);
        }
    }

    public IReadOnlyList<(int NpcId, float X, float Y)> LiveMobs()
    {
        lock (_gate)
        {
            return _liveMobs.Values.ToArray();
        }
    }

    public IReadOnlyList<(int NpcId, float X, float Y)> RecentMobPositions()
    {
        lock (_gate)
        {
            return _mobPositions.ToArray();
        }
    }

    /// <summary>A new map: what was learnt about the old one no longer applies.</summary>
    public void ForgetPlace()
    {
        lock (_gate)
        {
            _position = null;
            _mobPositions.Clear();
            _liveMobs.Clear();
            _recentSpawns.Clear();
        }
    }

    private (int EntityId, DateTime At)? _target;

    /// <summary>The monster the local player marked last (tab or click), with the time it was marked; null before any or after it died.</summary>
    public (int EntityId, int? NpcId, DateTime At)? LocalTarget
    {
        get
        {
            lock (_gate)
            {
                return _target is { } t ? (t.EntityId, _npcIds.TryGetValue(t.EntityId, out int npc) ? npc : null, t.At) : null;
            }
        }
    }

    public void SetLocalTarget(int entityId)
    {
        lock (_gate)
        {
            _target = (entityId, DateTime.UtcNow);
        }
    }

    public void ClearLocalTarget(int entityId)
    {
        lock (_gate)
        {
            if (_target is { } t && t.EntityId == entityId)
            {
                _target = null;
            }
        }
    }

    /// <summary>The NPC id a monster was announced with, when its spawn frame was seen.</summary>
    public int? NpcIdOf(int entityId)
    {
        lock (_gate)
        {
            return _npcIds.TryGetValue(entityId, out int npcId) ? npcId : null;
        }
    }

    private IReadOnlyList<Aion2SpeciesKnowledge>? _species;

    public IReadOnlyList<Aion2SpeciesKnowledge> LocalSpecies
    {
        get
        {
            lock (_gate)
            {
                return _species ?? Array.Empty<Aion2SpeciesKnowledge>();
            }
        }
    }

    private IReadOnlyList<Aion2TitleSlot>? _titles;

    /// <summary>The titles the local player wears (slot 1..3); empty before the game sent them.</summary>
    public IReadOnlyList<Aion2TitleSlot> LocalTitles
    {
        get
        {
            lock (_gate)
            {
                return _titles ?? Array.Empty<Aion2TitleSlot>();
            }
        }
    }

    public void SetLocalTitles(IReadOnlyList<Aion2TitleSlot> titles)
    {
        lock (_gate)
        {
            _titles = titles;
            _listsAt = DateTime.UtcNow;
        }

        NotifyCharacterChanged();
    }

    private Dictionary<int, int>? _attributes;
    private Aion2Wing? _wing;
    private int? _activePet;

    /// <summary>The base attributes (ids 1..6) and Lord values (7..17) of the local player, as the stat frame
    /// (<see cref="OpcodeFamily.Attributes"/>) last reported them; null before it arrived.</summary>
    public IReadOnlyDictionary<int, int>? LocalAttributes
    {
        get
        {
            lock (_gate)
            {
                return _attributes is null ? null : new Dictionary<int, int>(_attributes);
            }
        }
    }

    /// <summary>Merges the ids of a stat frame into the known table (a frame lists only the non-zero values, so a
    /// first frame starts from zeros for every id of <see cref="Aion2Attributes.FirstId"/>..<see cref="Aion2Attributes.LastId"/>).</summary>
    public void SetLocalAttributes(IReadOnlyDictionary<int, int> values)
    {
        lock (_gate)
        {
            if (_attributes is null)
            {
                _attributes = new Dictionary<int, int>();
                for (int id = Aion2Attributes.FirstId; id <= Aion2Attributes.LastId; id++)
                {
                    _attributes[id] = 0;
                }
            }

            foreach (var pair in values)
            {
                _attributes[pair.Key] = pair.Value;
            }

            _listsAt = DateTime.UtcNow;
        }

        NotifyCharacterChanged();
    }

    /// <summary>The wing and wing skin of the local player; null when the login record did not carry them.</summary>
    public Aion2Wing? LocalWing
    {
        get
        {
            lock (_gate)
            {
                return _wing;
            }
        }
    }

    public void SetLocalWing(Aion2Wing wing)
    {
        lock (_gate)
        {
            _wing = wing;
        }

        NotifyCharacterChanged();
    }

    /// <summary>The species id of the pet the local player has summoned (0 = none); null before the pet list arrived.</summary>
    public int? LocalActivePet
    {
        get
        {
            lock (_gate)
            {
                return _activePet;
            }
        }
    }

    public void SetLocalActivePet(int petSpecies)
    {
        bool changed;
        lock (_gate)
        {
            changed = _activePet != petSpecies;
            _activePet = petSpecies;
        }

        if (changed)
        {
            NotifyCharacterChanged();
        }
    }

    /// <summary>The pet circles of the local player: one per species, the quality of every effect slot of page 1.</summary>
    public IReadOnlyList<Aion2Pet> LocalPets
    {
        get
        {
            lock (_gate)
            {
                return (_species ?? Array.Empty<Aion2SpeciesKnowledge>())
                    .Select(k =>
                    {
                        var page = k.Effects.Where(e => e.Page == 1).ToList();
                        var kinds = new int[page.Count == 0 ? 0 : page.Max(e => e.Slot) + 1];
                        foreach (var e in page)
                        {
                            kinds[e.Slot] = e.Kind;
                        }

                        return new Aion2Pet(k.SpeciesId, k.Level, kinds);
                    })
                    .ToList();
            }
        }
    }

    public void SetLocalSpecies(IReadOnlyList<Aion2SpeciesKnowledge> species)
    {
        lock (_gate)
        {
            _species = species;
            _listsAt = DateTime.UtcNow;
        }

        NotifyCharacterChanged();
    }

    public void SetLocalDaevanion(IReadOnlyList<Aion2DaevanionBoard> boards)
    {
        lock (_gate)
        {
            _daevanion = boards;
            _listsAt = DateTime.UtcNow;
        }

        NotifyCharacterChanged();
    }

    public void SetLocalSkills(IReadOnlyList<Aion2SkillEntry> skills)
    {
        lock (_gate)
        {
            // The game re-sends the list on a zone change, and not every zone sends every skill (a stigma
            // went missing after one): what an earlier list had and the new one lacks is kept, what the
            // new one has wins.
            _listsAt = DateTime.UtcNow;
            // A list can arrive before the new character's own record (and so before the twink reset
            // above could run): the old character's class skills (id prefix 10-19) would then stay
            // merged into the new one for good. The class the new list is mostly made of decides.
            int? classPrefix = skills.Select(s => s.SkillId / 1_000_000).Where(k => k is >= 10 and < 20)
                .GroupBy(k => k).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
            var merged = (_skills ?? Array.Empty<Aion2SkillEntry>())
                .Where(old => skills.All(s => s.SkillId != old.SkillId))
                .Where(old => classPrefix is null || old.SkillId / 1_000_000 is < 10 or >= 20 || old.SkillId / 1_000_000 == classPrefix)
                .Concat(skills);
            _skills = merged.Select(s => s with { Stigma = _stigmas.Contains(s.SkillId), Equipped = _bar.Contains(s.SkillId) }).ToList();
        }

        NotifyCharacterChanged();
    }

    private readonly HashSet<int> _stigmas = new();

    /// <summary>The skill ids the game lists as stigmas; marks them in the skill list whichever of the two
    /// login frames arrives first.</summary>
    public void SetLocalStigmas(IReadOnlySet<int> ids, IReadOnlySet<int> listed)
    {
        lock (_gate)
        {
            // A skill the new frame does not list at all keeps its earlier flag (see SetLocalSkills).
            _stigmas.RemoveWhere(listed.Contains);
            _stigmas.UnionWith(ids);
            if (_skills is not null)
            {
                _skills = _skills.Select(s => s with { Stigma = _stigmas.Contains(s.SkillId) }).ToList();
            }
        }

        NotifyCharacterChanged();
    }

    private readonly HashSet<int> _bar = new();

    /// <summary>The base skill ids on the first macro page of the skill bar (what the game's skill window
    /// shows as equipped); replaces the previous bar, since the frame lists all of it.</summary>
    public void SetLocalBar(IReadOnlySet<int> ids)
    {
        lock (_gate)
        {
            _bar.Clear();
            _bar.UnionWith(ids);
            if (_skills is not null)
            {
                _skills = _skills.Select(s => s with { Equipped = _bar.Contains(s.SkillId) }).ToList();
            }
        }

        NotifyCharacterChanged();
    }

    private readonly Dictionary<string, Aion2InspectedPlayer> _inspected = new(StringComparer.Ordinal);

    /// <summary>Remembers a character window of another player; a newer one for the same name replaces it.</summary>
    public void SetInspected(Aion2InspectedPlayer player)
    {
        lock (_gate)
        {
            _inspected[player.Name] = player;
        }

        InspectedChanged?.Invoke();
    }

    /// <summary>Raised after a character window was remembered (the owner saves the list).</summary>
    public event Action? InspectedChanged;

    /// <summary>Takes over windows saved earlier; one the live stream already delivered is newer and wins.</summary>
    public void RestoreInspected(IEnumerable<Aion2InspectedPlayer> players)
    {
        lock (_gate)
        {
            foreach (var player in players)
            {
                _inspected.TryAdd(player.Name, player);
            }
        }
    }

    public IReadOnlyList<Aion2InspectedPlayer> InspectedPlayers()
    {
        lock (_gate)
        {
            return _inspected.Values.ToList();
        }
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

    // Entity id -> NPC id of every monster the game announced, for its name (see MonsterNameFor).
    private readonly Dictionary<int, int> _npcIds = new();

    /// <summary>The name of a monster from its NPC id (see <see cref="Protocol.Aion2Npcs"/>), in the app's language; null for a
    /// player, a summon the game did not name, or an NPC the table does not list. Apart from <see cref="NameFor"/>, which
    /// the player recognition relies on.</summary>
    public string? MonsterNameFor(int entityId)
    {
        int npcId;
        lock (_gate)
        {
            if (!_npcIds.TryGetValue(entityId, out npcId))
            {
                return null;
            }
        }

        return Protocol.Aion2Npcs.NameOf(npcId, Protocol.Aion2SkillNames.Language);
    }

    // Entity id -> NPC id, kept only for monsters that are bosses (see Aion2BossCatalog).
    private readonly Dictionary<int, int> _bossNpcs = new();

    /// <summary>Remembers that the monster with this entity id is the boss with this NPC id. Called
    /// when the game announces a monster; ids that are no boss are ignored.</summary>
    public void RegisterNpc(int entityId, int npcId)
    {
        lock (_gate)
        {
            _npcIds[entityId] = npcId;
        }

        if (Protocol.Aion2BossCatalog.Find(npcId) is null)
        {
            return;
        }

        lock (_gate)
        {
            _bossNpcs[entityId] = npcId;
        }
    }

    /// <summary>Every entity's hit points as the server reports them, and when a monster was reset
    /// to full health (a wipe and retry under the same entity id).</summary>
    public Aion2HitPoints HitPoints { get; } = new();

    // Every entity announced by the monster-appears frame: monsters and summons, never players.
    private readonly HashSet<int> _spawned = new();

    /// <summary>Notes that the server announced this entity as a monster (or a summon). An entity id is
    /// reused, so a new spawn wipes the removal of an earlier one.</summary>
    public void NoteSpawned(int entityId)
    {
        lock (_gate)
        {
            _spawned.Add(entityId);
            _removed.Remove(entityId);
        }
    }

    /// <summary>Why and when a monster disappeared; reason 3 = it died, 7 = it vanished alive.</summary>
    public readonly record struct Removal(DateTime At, byte Reason);

    public const byte RemovedDead = 3;

    private readonly Dictionary<int, Removal> _removed = new();

    public void NoteRemoved(int entityId, byte reason, DateTime at)
    {
        lock (_gate)
        {
            _removed[entityId] = new Removal(at, reason);
            _liveMobs.Remove(entityId);
        }
    }

    /// <summary>The removal of the monster's current spawn, or null while it is still there.</summary>
    public Removal? RemovalOf(int entityId)
    {
        lock (_gate)
        {
            return _removed.TryGetValue(entityId, out Removal removal) ? removal : null;
        }
    }

    /// <summary>True for an entity the server announced with the monster-appears frame (monsters
    /// and summons, never players).</summary>
    public bool IsSpawned(int entityId)
    {
        lock (_gate)
        {
            return _spawned.Contains(entityId);
        }
    }

    /// <summary>The ids of the party members (see <see cref="PartyNames"/>) who play this class.</summary>
    public IReadOnlyList<int> PartyMemberIdsOfClass(string className)
    {
        var party = PartyNames;
        lock (_gate)
        {
            return party.Where(name => _ids.ContainsKey(name)).Select(name => _ids[name]).Distinct()
                .Where(id => _classVotes.TryGetValue(id, out var votes) && votes.MaxBy(v => v.Value).Key == className)
                .ToList();
        }
    }

    /// <summary>True for an entity the server announced as a monster that is nobody's summon.</summary>
    public bool IsKnownMonster(int entityId)
    {
        lock (_gate)
        {
            return _spawned.Contains(entityId) && !_summonOwners.ContainsKey(entityId) && !_summonOwnerNames.ContainsKey(entityId);
        }
    }

    // Summoned entity id -> the player who summoned it (see Aion2FrameDecoder.DecodeNpcSpawn).
    private readonly Dictionary<int, int> _summonOwners = new();

    // Owners found since the last DrainResolvedOwners: hits the summon dealt before (credited to its
    // own id while nobody knew whose it was) can be handed to its owner.
    private readonly List<(int Summon, int Owner)> _resolvedOwners = new();

    /// <summary>The summon owners found since the last call - for re-crediting the hits a summon
    /// dealt before its owner was known (a meter started mid-fight, its first seconds).</summary>
    public IReadOnlyList<(int Summon, int Owner)> DrainResolvedOwners()
    {
        lock (_gate)
        {
            var drained = _resolvedOwners.ToList();
            _resolvedOwners.Clear();
            return drained;
        }
    }

    /// <summary>Records who summoned an entity, or (null) that it is nobody's summon - entity ids
    /// are reused, so a later spawn under the same id clears an earlier owner.</summary>
    public void SetSummonOwner(int entityId, int? ownerId)
    {
        lock (_gate)
        {
            if (ownerId is int owner)
            {
                if ((!_summonOwners.TryGetValue(entityId, out int known) || known != owner) && _resolvedOwners.Count < 4096)
                {
                    _resolvedOwners.Add((entityId, owner));
                }

                _summonOwners[entityId] = owner;
            }
            else
            {
                _summonOwners.Remove(entityId);
            }
        }
    }

    /// <summary>The player who summoned this entity, or null when it is not a known summon.</summary>
    // Summoned entity id -> its owner's name, for summons announced by name (see
    // Aion2FrameDecoder.DecodeNpcSpawn); resolved through the name -> id map when asked.
    private readonly Dictionary<int, string> _summonOwnerNames = new();

    /// <summary>Records the name a spawned entity carries - a summon's owner (a Cleric's Divine
    /// Aura carries "Psefon"); null clears it, the id being reused by something unnamed.</summary>
    public void SetSummonOwnerName(int entityId, string? ownerName)
    {
        lock (_gate)
        {
            if (ownerName is null)
            {
                _summonOwnerNames.Remove(entityId);
            }
            else
            {
                _summonOwnerNames[entityId] = ownerName;
            }
        }
    }

    public int? SummonOwnerOf(int entityId)
    {
        lock (_gate)
        {
            if (_summonOwners.TryGetValue(entityId, out int owner))
            {
                return owner;
            }

            // Owner known by name: the player of that name, when it is a player and not the entity itself.
            return _summonOwnerNames.TryGetValue(entityId, out string? name) && _ids.TryGetValue(name, out int byName) && byName != entityId
                ? byName
                : null;
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

    /// <summary>The faction of a player: that of his server, else the faction byte of his "player appeared" frame; null otherwise.
    /// The low bits of the class code are NOT a faction (Elyos characters carry 1 and 2 there) and are not used.</summary>
    public string? FactionOf(int id)
    {
        // The server the player named (every player is announced with his server id): Elyos servers are 1xxx, Asmodian ones 2xxx -
        // the faction of a player is the faction of his server (Elyos and Asmodians never share a server, only the worlds are matched).
        if (ServerIdOf(id) is int server && server / 1000 is 1 or 2)
        {
            return server / 1000 == 1 ? "Elyos" : "Asmodian";
        }

        lock (_gate)
        {
            if (_factionById.TryGetValue(id, out int announced))
            {
                return announced == 1 ? "Elyos" : "Asmodian";
            }
        }

        return null;
    }

    private readonly Dictionary<int, int> _factionById = new();

    /// <summary>The faction byte of the "player appeared" frame: 1 Elyos, 2 Asmodian.</summary>
    public void NoteFaction(int id, int faction)
    {
        lock (_gate)
        {
            _factionById[id] = faction;
        }
    }

    private readonly Dictionary<string, int> _idOfIdentity = new(StringComparer.Ordinal);

    /// <summary>A player is one name on one server (a character name is unique per server): when the game announces him under another
    /// combat id (a map change gives everybody new ones), the old id is the same player and <see cref="IdentityMoved"/> says so.</summary>
    public void NoteIdentity(int id, string name, int serverId)
    {
        if (serverId is < 1000 or > 9999 || name.Length < 2)
        {
            return;
        }

        int? moved = null;
        lock (_gate)
        {
            string key = serverId + "|" + name;
            if (_idOfIdentity.TryGetValue(key, out int previous) && previous != id)
            {
                moved = previous;
            }

            _idOfIdentity[key] = id;
            if (moved is not null)
            {
                IdentityMovesSeen++;
            }
        }

        if (moved is int old)
        {
            IdentityMoved?.Invoke(old, id);
        }
    }

    public int IdentityMovesSeen { get; private set; }

    /// <summary>A player known under one combat id was announced under another (same name, same server).</summary>
    public event Action<int, int>? IdentityMoved;

    private readonly Dictionary<string, int> _serverOfName = new(StringComparer.Ordinal);

    /// <summary>A player's server id as the game announced it next to his name (appearance and party frames).</summary>
    public void NoteServerOfName(string name, int serverId)
    {
        if (serverId is >= 1000 and <= 9999)
        {
            lock (_gate)
            {
                _serverOfName[name] = serverId;
            }
        }
    }

    /// <summary>The server of a player: the own one from the character record, another one's from the frames that named him.</summary>
    public int? ServerIdOf(int id)
    {
        lock (_gate)
        {
            if (IsLocalPlayer(id) && _character is { ServerId: > 0 } own)
            {
                return own.ServerId;
            }

            return _names.TryGetValue(id, out string? name) && _serverOfName.TryGetValue(name, out int server) ? server : null;
        }
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

    // Party member name -> when a roster frame last listed it.
    private readonly Dictionary<string, DateTime> _partySeen = new(StringComparer.Ordinal);
    private DateTime _lastPartyFrame;

    /// <summary>How long a member stays in the party after the last roster frame naming it: the
    /// frames are re-sent every few seconds, but one frame does not always list everybody.</summary>
    private static readonly TimeSpan PartyMemory = TimeSpan.FromSeconds(90);

    /// <summary>Notes the members one party roster frame lists (the local player included).</summary>
    public void NoteParty(IReadOnlyCollection<string> names, DateTime at)
    {
        lock (_gate)
        {
            foreach (string name in names)
            {
                _partySeen[name] = at;
            }

            _lastPartyFrame = at;
            MatchPartyMembersByClass();
        }
    }

    // Party member name -> class, from the roster's class code.
    private readonly Dictionary<string, string> _partyClasses = new(StringComparer.Ordinal);

    // Monsters the local player or a named party member hit, and the unnamed players who hit one too.
    private readonly HashSet<int> _partyTargets = new();
    private readonly HashSet<int> _fightingAlongside = new();

    // Unnamed players fighting alongside the party -> the skills (base ids) they were seen using.
    private readonly Dictionary<int, HashSet<int>> _skillsSeen = new();

    /// <summary>A player uses many skills; a spirit summoned before the meter started (owner
    /// unknown) a few: 16 against 3 on a Krao Cave capture replayed from mid-fight.</summary>
    private const int MinSkillsOfAPlayer = 4;

    /// <summary>True when a name is registered for this id - a player, never a summon.</summary>
    public bool HasName(int id)
    {
        lock (_gate)
        {
            return _names.ContainsKey(id);
        }
    }

    /// <summary>The party's players of a class by id: the members a frame named, and the local
    /// player when it plays that class (its own name may be known only from Settings).</summary>
    public IReadOnlyList<int> PartyPlayerIdsOfClass(string className)
    {
        var ids = PartyMemberIdsOfClass(className).ToList();
        if (InferLocalPlayer() is int local && !ids.Contains(local) && ClassOf(local) == className)
        {
            ids.Add(local);
        }

        return ids;
    }

    /// <summary>True for a player whose name is known while the party is, and who is not in it - a
    /// stranger nearby in the open world.</summary>
    public bool IsNamedOutsideParty(int id)
    {
        lock (_gate)
        {
            if (!_names.TryGetValue(id, out string? name))
            {
                return false;
            }

            var party = CurrentPartyNames();
            return party.Count > 0 && !party.Contains(name);
        }
    }

    /// <summary>Notes a party member's class, as the roster gives it.</summary>
    public void NotePartyClass(string name, string className)
    {
        lock (_gate)
        {
            _partyClasses[name] = className;
        }
    }

    /// <summary>Notes a player's hit on a monster, for <see cref="MatchPartyMembersByClass"/>.</summary>
    public void NoteMonsterHit(int playerId, int monsterId, int skillId)
    {
        lock (_gate)
        {
            bool partySide = _names.TryGetValue(playerId, out string? name)
                ? CurrentPartyNames().Contains(name)
                : IsLocalPlayer(playerId) || InferLocalPlayer() == playerId;
            if (partySide)
            {
                if (_partyTargets.Count > 4096)
                {
                    _partyTargets.Clear();
                }

                _partyTargets.Add(monsterId);
            }
            else if (!_names.ContainsKey(playerId) && !_spawned.Contains(playerId) && _partyTargets.Contains(monsterId))
            {
                _fightingAlongside.Add(playerId);
                if (!_skillsSeen.TryGetValue(playerId, out var skills))
                {
                    skills = new HashSet<int>();
                    _skillsSeen[playerId] = skills;
                }

                if (skills.Add(skillId / 10000) && skills.Count == MinSkillsOfAPlayer)
                {
                    MatchPartyMembersByClass();
                }
            }
        }
    }

    /// <summary>
    /// A player's name arrives only when they "appear" (zone entry, teleport): started inside a
    /// dungeon, the meter knows the party from the roster - names and classes, no combat ids - while
    /// the members fight as unnamed ids. When the party has exactly one member of a class still
    /// without an id, and exactly one unnamed player of that class fights the party's monsters, they
    /// are the same character. Summons do not count (an unclaimed Divine Aura or Bittercold Wind
    /// casts its class's skills too). With two such members or two such players nothing is
    /// guessed; the local player is left to its own rules. (Draupnir and Krao Cave captures
    /// replayed from mid-fight, 2026-10-02.)
    /// </summary>
    private void MatchPartyMembersByClass()
    {
        var party = CurrentPartyNames();
        int? local = _explicitLocalId >= 0 ? _explicitLocalId : InferLocalPlayer();
        string? localName = _character?.Name ?? _configuredLocalName;
        var unnamedMembers = party
            .Where(n => !_ids.ContainsKey(n) && n != localName && _partyClasses.ContainsKey(n))
            .GroupBy(n => _partyClasses[n])
            .Where(g => g.Count() == 1)
            .ToList();
        foreach (var member in unnamedMembers)
        {
            // A spirit summoned before the meter started has no known owner and casts its class's
            // skills too, but few different ones (MinSkillsOfAPlayer). Then one candidate, or one
            // clearly ahead (three times the next one's casts), the same rule as InferLocalPlayer.
            var candidates = _fightingAlongside
                .Where(id => !_names.ContainsKey(id) && id != local && !_spawned.Contains(id)
                    && _skillsSeen.TryGetValue(id, out var skills) && skills.Count >= MinSkillsOfAPlayer
                    && _classVotes.TryGetValue(id, out var votes) && votes.MaxBy(v => v.Value).Key == member.Key)
                .Select(id => (Id: id, Casts: _classVotes[id].Values.Sum()))
                .OrderByDescending(c => c.Casts)
                .ToList();
            if (candidates.Count == 1 || (candidates.Count > 1 && candidates[0].Casts >= 3 * candidates[1].Casts))
            {
                Register(candidates[0].Id, member.First());
                _fightingAlongside.Remove(candidates[0].Id);
            }
        }
    }

    /// <summary>Names in the local player's party: listed by a roster frame within
    /// <see cref="PartyMemory"/> of the latest one. Empty when no roster has arrived yet.</summary>
    public IReadOnlySet<string> PartyNames
    {
        get
        {
            lock (_gate)
            {
                return CurrentPartyNames();
            }
        }
    }

    private HashSet<string> CurrentPartyNames() =>
        _partySeen.Where(kv => _lastPartyFrame - kv.Value <= PartyMemory).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>A name the roster shows that is not a party member - the guild name, which every
    /// member's nickname frame repeats after its own name.</summary>
    public void NoteNonPlayerName(string name)
    {
        lock (_gate)
        {
            _notPlayers.Add(name);
        }
    }

    // Per entity: a decaying count of its detailed-stats frames (see NoteDetailedStats).
    private readonly Dictionary<int, double> _detailedStats = new();

    /// <summary>
    /// The server sends an entity's detailed stats (the 4-byte group of the stats frame) to that
    /// player alone: on four captures (2026-10-02) the local player received 651 to 1,477 of them,
    /// any other entity 0 to 5. Older counts decay, so after a zone change hands the local player a
    /// new id, the new one takes over within a few frames.
    /// </summary>
    public void NoteDetailedStats(int entityId)
    {
        lock (_gate)
        {
            foreach (int id in _detailedStats.Keys.ToList())
            {
                _detailedStats[id] *= 0.95;
            }

            _detailedStats[entityId] = _detailedStats.GetValueOrDefault(entityId) + 1;
        }
    }

    /// <summary>
    /// The local player, worked out from the stream: first the entity receiving the detailed-stats
    /// frames (see <see cref="NoteDetailedStats"/>) - reliable in a crowd; else the object seen
    /// casting class skills that never got a nickname frame. Only claimed when it is unambiguous - one
    /// such object, or one clearly dominant - otherwise null and nobody is called "you".
    /// </summary>
    public int? InferLocalPlayer()
    {
        lock (_gate)
        {
            var byStats = _detailedStats.OrderByDescending(kv => kv.Value).Take(2).ToList();
            if (byStats.Count > 0 && byStats[0].Value >= 5 && (byStats.Count == 1 || byStats[0].Value >= 3 * byStats[1].Value))
            {
                return byStats[0].Key;
            }

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

            // The local player is never announced to itself, so its id has no name of its own until
            // the character record (login, zone change) arrives. Until then: the name set in
            // Settings, else the character saved from the last login, else the party roster's
            // leftover name - solo, only the first two exist, and "Player #id" used to stay.
            // Not once the local player is known for certain (a session frame's id) or the name is
            // already registered to another object: the inference then only picked the busiest
            // unnamed caster, a team mate, who used to show up as a second row under one's own name.
            if (registered is null && InferLocalPlayer() == id && !LocalPlayerKnownElsewhere(id))
            {
                // The roster's leftover name is safe here: the local player is not named yet, so its
                // own name is still among the leftovers, and a single leftover is it.
                registered = _configuredLocalName
                    ?? (_character is { Restored: true } saved && saved.Name.Length > 0 ? saved.Name : null)
                    ?? LocalRosterName();
            }
        }

        return registered ?? (ClassOf(id) is not null ? $"Player #{id}" : null);
    }

    // Called under _gate. True when something other than `id` is the local player: the explicit id
    // names someone else, or the configured / saved character name already belongs to another object.
    private bool LocalPlayerKnownElsewhere(int id)
    {
        if (_explicitLocalId >= 0)
        {
            return _explicitLocalId != id;
        }

        string? own = _configuredLocalName ?? (_character is { Restored: true } saved ? saved.Name : null);
        return own is not null && _ids.TryGetValue(own, out int owner) && owner != id;
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
