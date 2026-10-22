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
/// <summary><paramref name="Stones"/>: the choice in each specialisation slot of an active skill (1-5 = the five stones of that skill, 0 = empty); null = not read.</summary>
public sealed record Aion2SkillEntry(int SkillId, int Level, int BaseLevel, bool Stigma = false, bool Equipped = false, IReadOnlyList<int>? Stones = null);

/// <summary>One of the skill presets of the local player (the game keeps up to three; the server only ever sends the active one,
/// so the others are known from the time they were last active).</summary>
public sealed record Aion2PresetInfo(int Index, bool Active, DateTime SeenAt, IReadOnlyList<Aion2SkillEntry> Skills);

/// <summary>What the "player appeared" frame says about another player: class and the class bits (decoded
/// from its class code; the low two bits are NOT a faction - Elyos characters carry 1 and 2) and the visible
/// equipment (no enchant levels in that list).</summary>
public sealed record Aion2SeenProfile(
    int? ClassId,
    int? ClassBits,
    IReadOnlyList<Aion2EquippedItem> Gear,
    int? Level = null,
    int? ItemLevel = null,
    // Read for the screen only: the game's combat power as the frame carries it (the number is fixed in the 10 against 10 arena). Never uploaded.
    int? CombatPower = null,
    // The pieces the frame lists with the sum of enchant level and excess level (0 = not told); for the screen only, the upload uses <see cref="Gear"/>.
    IReadOnlyList<Aion2WornItem>? Worn = null);

/// <summary>A worn piece as the newer "player appeared" frame lists it: its position (1 main hand, 2 helmet, 3 shoulder, 4 chest, 5 legs, 6 gloves, 7 boots, 8 cape,
/// 9 necklace, 10 and 11 earrings), its id and the enchant level plus the excess level together (the two are not told apart; 0 = not told).</summary>
public sealed record Aion2WornItem(int SlotIndex, int ItemId, int EnchantTotal);

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
    IReadOnlyList<Aion2BoardCount>? BoardCounts = null,
    // Faction ("Elyos"/"Asmodian") and server id of the player, as far as the stream named him when his window was opened (or later): kept with the
    // window because a restart of the meter forgets the stream, and the community server leaves out a player without them.
    string? Faction = null,
    int? ServerId = null,
    // When this window went to the community server (by itself, see MainWindow.UploadInspectedAsync); null = not yet. Kept with the window so a restart does not send it twice.
    DateTime? UploadedAt = null);

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
                _stats = null;
                _wing = null;
                _activePet = null;
                _stigmas.Clear();
                _bar.Clear();
                _stones.Clear();
                _presets.Clear();
                _activePreset = 1;
                _presetKnown = false;
                _liveLists = false;
                _pruneStigmas = false;
            }

            DropRestoredListsOfOthers(info.Name);

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
            _skills = saved.Skills.Select(s => new Aion2SkillEntry(s.Id, s.Level, s.BaseLevel, s.Stigma, s.Equipped, s.Stones)).ToList();
            _activePreset = saved.ActivePreset is >= 1 and <= 9 ? saved.ActivePreset : 1;
            // A file from before the presets (1.5.x) does not say which preset its lists belong to.
            _presetKnown = saved.Presets.Count > 0;
            _liveLists = false;
            _presetAt = saved.SavedAt;
            _stigmas.Clear();
            _stigmas.UnionWith(_skills.Where(k => k.Stigma).Select(k => k.SkillId));
            _stones.Clear();
            foreach (var k in _skills.Where(k => k.Stones is not null))
            {
                _stones[k.SkillId] = k.Stones!.ToArray();
            }

            _presets.Clear();
            foreach (var preset in saved.Presets.Where(x => x.Index != _activePreset && x.Index is >= 1 and <= 9))
            {
                var list = preset.Skills.Select(k => new Aion2SkillEntry(k.Id, k.Level, k.BaseLevel, k.Stigma, false, k.Stones)).ToList();
                _presets[preset.Index] = new PresetSlot
                {
                    Skills = list,
                    Stigmas = list.Where(k => k.Stigma).Select(k => k.SkillId).ToHashSet(),
                    Stones = list.Where(k => k.Stones is not null).ToDictionary(k => k.SkillId, k => k.Stones!.ToArray()),
                    At = preset.SeenAt,
                };
            }

            _daevanion = saved.Daevanion.Select(b => new Aion2DaevanionBoard(b.Board, b.Nodes)).ToList();
            _species = saved.Species.Select(k => new Aion2SpeciesKnowledge(k.Id, k.Level, k.Progress,
                k.Effects.Select(e => new Aion2SpeciesEffect(e.Page, e.Slot, e.Stat, e.Value, e.Kind)).ToList())).ToList();
            _titles = saved.Titles.Select(t => new Aion2TitleSlot(t.Slot, t.TitleId)).ToList();
            _attributes = saved.Attributes is { Count: > 0 } ? new Dictionary<int, int>(saved.Attributes) : null;
            _wing = saved.WingId is int savedWing ? new Aion2Wing(savedWing, saved.WingSkinId ?? 0) : null;
            _activePet = saved.ActivePet;
            _petStates = saved.Pets.Select(p => new Aion2PetState(p.Id, p.Level, p.Progress)).ToList();
            _restoredOwner = saved.Name;
            _restoredLists = (_skills, _fullEquipment, _daevanion, _species, _titles, _petStates, _attributes, _wing, _activePet);
        }
    }

    // The lists RestoreFrom took from the file belong to the character that was saved. A live list the game sends later is a new
    // object; one still standing as restored when ANOTHER character logs in is that character's leftover and must go.
    private string? _restoredOwner;

    /// <summary>Called under _gate when the game names the character: lists that are still the restored ones and belong to
    /// another character are dropped, whatever the timing of the live lists.</summary>
    private void DropRestoredListsOfOthers(string liveName)
    {
        string? owner = _restoredOwner;
        _restoredOwner = null;
        if (owner is null || owner == liveName || _restoredLists is not { } was)
        {
            _restoredLists = null;
            return;
        }

        _restoredLists = null;
        if (ReferenceEquals(_skills, was.Skills)) { _skills = null; }
        if (ReferenceEquals(_fullEquipment, was.Equipment)) { _fullEquipment = null; }
        if (ReferenceEquals(_daevanion, was.Daevanion)) { _daevanion = null; }
        if (ReferenceEquals(_species, was.Species)) { _species = null; }
        if (ReferenceEquals(_titles, was.Titles)) { _titles = null; }
        if (ReferenceEquals(_petStates, was.PetStates)) { _petStates = null; }
        if (ReferenceEquals(_attributes, was.Attributes)) { _attributes = null; }
        if (Equals(_wing, was.Wing)) { _wing = null; }
        if (_activePet == was.ActivePet) { _activePet = null; }
    }

    private (object? Skills, object? Equipment, object? Daevanion, object? Species, object? Titles, object? PetStates, object? Attributes, Aion2Wing? Wing, int? ActivePet)? _restoredLists;

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
                Skills = (_skills ?? Array.Empty<Aion2SkillEntry>()).Select(s => new Aion2SavedCharacter.SavedSkill(s.SkillId, s.Level, s.BaseLevel, s.Stigma, s.Equipped, s.Stones?.ToArray())).ToList(),
                ActivePreset = _activePreset,
                Presets = PresetsLocked().Select(x => new Aion2SavedCharacter.SavedPreset(x.Index, x.SeenAt,
                    x.Skills.Select(s => new Aion2SavedCharacter.SavedSkill(s.SkillId, s.Level, s.BaseLevel, s.Stigma, s.Equipped, s.Stones?.ToArray())).ToList())).ToList(),
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

    private readonly Dictionary<int, (DateTime At, IReadOnlyList<Aion2ShopPet> Pets)> _shopPets = new();

    /// <summary>The pets the Black Cloud shop offers, from the tabs the player opened (the shop sends a tab when it is opened), newest tab first by
    /// tab; each tab is dropped five minutes after its frame (the shop renews its offers every hour, a closed shop sends nothing).</summary>
    public IReadOnlyList<Aion2ShopPet> ShopPets
    {
        get
        {
            lock (_gate)
            {
                var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(5);
                return _shopPets.Where(kv => kv.Value.At >= cutoff).OrderBy(kv => kv.Key).SelectMany(kv => kv.Value.Pets).ToList();
            }
        }
    }

    /// <summary>When the newest shop tab arrived (within the five minutes the offers are kept), else null: the shop was opened, or a tab of it, then.</summary>
    public DateTime? ShopFrameAt
    {
        get
        {
            lock (_gate)
            {
                var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(5);
                var fresh = _shopPets.Values.Where(v => v.At >= cutoff).Select(v => v.At).ToList();
                return fresh.Count > 0 ? fresh.Max() : null;
            }
        }
    }

    /// <summary>A shop tab arrived: replaces what the same tab offered before (an empty list, e.g. the skin tab, clears it).</summary>
    public void NoteShopTab(int tab, IReadOnlyList<Aion2ShopPet> pets)
    {
        lock (_gate)
        {
            _shopPets[tab] = (DateTime.UtcNow, pets);
        }
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
                // an exact report stays the position for 6 s: the monsters' middle is only a guess and flips the view back and forth when it competes with it
                if (recent.Count >= 2 && (_position is null || now - _position.Value.At > TimeSpan.FromSeconds(6)) && (_position is null || recent.Max(s => s.At) > _position.Value.At))
                {
                    var xs = recent.Select(s => s.X).OrderBy(v => v).ToList();
                    var ys = recent.Select(s => s.Y).OrderBy(v => v).ToList();
                    float gx = xs[xs.Count / 2], gy = ys[ys.Count / 2];
                    if (GuessIsPlausible(gx, gy, now))
                    {
                        return (gx, gy, recent.Max(s => s.At));
                    }
                }

                // The game reports the player's own position only for some movements (not when a monster is targeted), so it can stay unknown
                // for a long time (after a relog, or standing still). The monsters around him, announced and not yet gone again, still
                // surround him (within the ~65 m range): their middle beats "unknown" and beats a report that is more than 15 s old.
                if ((_position is null || now - _position.Value.At > TimeSpan.FromSeconds(15)) && _liveMobs.Count >= 3)
                {
                    var xs = _liveMobs.Values.Select(m => m.X).OrderBy(v => v).ToList();
                    var ys = _liveMobs.Values.Select(m => m.Y).OrderBy(v => v).ToList();
                    if (GuessIsPlausible(xs[xs.Count / 2], ys[ys.Count / 2], now))
                    {
                        return (xs[xs.Count / 2], ys[ys.Count / 2], now);
                    }
                }

                return _position;
            }
        }
    }

    /// <summary>
    /// The middle of the monsters around the player is only a guess, and monsters far outside his range are announced too (a group that respawns
    /// 190 m away, seen at an obelisk: the guess jumped there every ten seconds and the map swung back and forth while he stood still). A guess that
    /// lies further from his last exact place than he could have walked since (10 m/s plus 30 m, for at most 12 s) is not taken; with no exact place
    /// known yet every guess counts.
    /// </summary>
    private bool GuessIsPlausible(float x, float y, DateTime now)
    {
        if (_position is not { } last)
        {
            return true;
        }

        double seconds = Math.Min((now - last.At).TotalSeconds, 12);
        double dx = x - last.X, dy = y - last.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= 3000 + 1000 * seconds;
    }

    public void NoteLocalPosition(float x, float y)
    {
        lock (_gate)
        {
            _position = (x, y, DateTime.UtcNow);
            _positionIsSpawn = false;
        }
    }

    private bool _positionIsSpawn;

    /// <summary>Where a zone frame put the player (a teleport or a loading screen): an exact place, but of the new zone, so a map change keeps it.</summary>
    public void NoteSpawnPosition(float x, float y)
    {
        lock (_gate)
        {
            _position = (x, y, DateTime.UtcNow);
            _positionIsSpawn = true;
            // the monsters and cubes announced so far belong to the place he left (the game does not always announce their end): the ones of the new place
            // follow this frame
            _env.Clear();
            _mobPositions.Clear();
            _liveMobs.Clear();
            _recentSpawns.Clear();
        }
    }

    private int _zoneId;

    /// <summary>The zone the last zone frame named (0 before any).</summary>
    public int ZoneId
    {
        get
        {
            lock (_gate)
            {
                return _zoneId;
            }
        }
    }

    public void NoteZone(int zoneId)
    {
        lock (_gate)
        {
            _zoneId = zoneId;
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
            if (_liveMobs.Count > 1000)
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

    private readonly Dictionary<int, (string Kind, string Key, float X, float Y, DateTime At)> _env = new();

    /// <summary>A collectible or Hidden Cube was announced by the game (env object spawn frame: it is in range and there).</summary>
    public void NoteEnv(int entityId, string kind, string key, float x, float y)
    {
        lock (_gate)
        {
            if (kind == "Cube" && !_env.ContainsKey(entityId))
            {
                CubeArrivals++;
            }

            _env[entityId] = (kind, key, x, y, DateTime.UtcNow);
            if (_env.Count > 3000)
            {
                _env.Clear(); // nothing is ever that close: the table lost its removals
            }
        }
    }

    /// <summary>The object is gone (gathered, opened, or out of range).</summary>
    public void ForgetEnv(int entityId)
    {
        lock (_gate)
        {
            _env.Remove(entityId);
        }
    }

    /// <summary>The collectibles in range right now (one that was never reported gone is dropped after 15 minutes).</summary>
    public IReadOnlyList<(string Kind, string Key, float X, float Y)> LiveEnv()
    {
        lock (_gate)
        {
            foreach (int stale in _env.Where(c => DateTime.UtcNow - c.Value.At > TimeSpan.FromMinutes(15)).Select(c => c.Key).ToList())
            {
                _env.Remove(stale);
            }

            return _env.Values.Select(c => (c.Kind, c.Key, c.X, c.Y)).ToArray();
        }
    }

    /// <summary>How many Hidden Cubes have come into range so far (a new entity each, a repeated announcement of the same one does not count): the map
    /// plays its arrival chime when the number grows (Stacy's idea, 17 October 2026).</summary>
    public int CubeArrivals { get; private set; }

    /// <summary>The militia guards announced around the player right now (see <see cref="Aion2Guards"/>).</summary>
    public IReadOnlyList<(float X, float Y)> LiveGuards() => LiveMobs().Where(m => Aion2Guards.IsGuard(m.NpcId)).Select(m => (m.X, m.Y)).ToArray();

    /// <summary>The Hidden Cubes in range right now.</summary>
    public IReadOnlyList<(float X, float Y)> LiveCubes() => LiveEnv().Where(e => e.Kind == "Cube").Select(e => (e.X, e.Y)).ToArray();

    public IReadOnlyList<(int NpcId, float X, float Y)> RecentMobPositions()
    {
        lock (_gate)
        {
            return _mobPositions.ToArray();
        }
    }

    /// <summary>A new map: what was learnt about the old one no longer applies. The place a zone frame put the player at is of the new map and stays
    /// (the character record that triggers this arrives a few seconds after the zone frame).</summary>
    public void ForgetPlace()
    {
        lock (_gate)
        {
            if (!(_positionIsSpawn && _position is { } spawn && DateTime.UtcNow - spawn.At < TimeSpan.FromSeconds(60)))
            {
                _position = null;
            }

            _env.Clear();
            _mobPositions.Clear();
            _liveMobs.Clear();
            _recentSpawns.Clear();
        }
    }

    private int _zoneEpoch;

    /// <summary>Counts the zone changes: what hangs on an entity of the zone left (the boss bar) must not outlive it.</summary>
    public int ZoneEpoch
    {
        get
        {
            lock (_gate)
            {
                return _zoneEpoch;
            }
        }
    }

    /// <summary>The player entered another zone: the marked monster and the last one hit are gone with the zone.</summary>
    public void ForgetFight()
    {
        lock (_gate)
        {
            _target = null;
            _lastHit = null;
            _zoneEpoch++;
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

    private (int EntityId, DateTime At)? _lastHit;

    /// <summary>The monster the local player hit last, with the time: stands in for the target when the player aims with the camera (no mark message is sent then).</summary>
    public (int EntityId, int? NpcId, DateTime At)? LocalHitTarget
    {
        get
        {
            lock (_gate)
            {
                return _lastHit is { } t ? (t.EntityId, _npcIds.TryGetValue(t.EntityId, out int npc) ? npc : null, t.At) : null;
            }
        }
    }

    public void NoteLocalHit(int entityId)
    {
        lock (_gate)
        {
            _lastHit = (entityId, DateTime.UtcNow);
        }
    }

    public void ClearLocalTarget(int entityId)
    {
        lock (_gate)
        {
            if (_lastHit is { } h && h.EntityId == entityId)
            {
                _lastHit = null;
            }

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
            // A frame with all three slots is the full set (login); a shorter one is taken as a change of just those
            // slots, so the titles it does not mention stay.
            if (titles.Count < 3 && _titles is { Count: > 0 })
            {
                _titles = _titles.Where(t => titles.All(n => n.Slot != t.Slot)).Concat(titles).OrderBy(t => t.Slot).ToList();
            }
            else
            {
                _titles = titles;
            }

            _listsAt = DateTime.UtcNow;
        }

        NotifyCharacterChanged();
    }

    private Dictionary<int, int>? _attributes;
    private Dictionary<int, int>? _stats;
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

    /// <summary>Every value of the stat frame (<see cref="OpcodeFamily.Attributes"/>) of the local player by stat id, the base attributes and
    /// Lord values included (the stat window's attack, damage boni and rates: see <see cref="Aion2StatIds"/>); null before it arrived.
    /// Not stored with the character file: the stat calculator needs the values of this session only.</summary>
    public IReadOnlyDictionary<int, int>? LocalStats
    {
        get
        {
            lock (_gate)
            {
                return _stats is null ? null : new Dictionary<int, int>(_stats);
            }
        }
    }

    /// <summary>Merges the ids of a stat frame into the stat table (a later frame lists only what changed).</summary>
    public void SetLocalStats(IReadOnlyDictionary<int, int> values)
    {
        lock (_gate)
        {
            _stats ??= new Dictionary<int, int>();
            foreach (var pair in values)
            {
                _stats[pair.Key] = pair.Value;
            }
        }
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
            _liveLists = true;
            var listed = merged.Select(k => k.SkillId).ToHashSet();
            if (_pruneStigmas)
            {
                // The first full list after a switch to a preset not seen before: a stigma it does not list is not in that preset.
                _stigmas.IntersectWith(listed);
                _pruneStigmas = false;
            }

            _skills = merged.Select(s => s with { Stigma = _stigmas.Contains(s.SkillId), Equipped = _bar.Contains(s.SkillId), Stones = StonesOf(s.SkillId) }).ToList();
        }

        NotifyCharacterChanged();
    }

    // ---- skill presets -------------------------------------------------------------------------------------------------
    // The server sends only the active preset. The skill list, stigmas and stone choices of the preset that was active are kept
    // in _skills/_stigmas/_stones; on a switch they go to _presets and the data of the new preset (or, if it was never seen, the
    // unchanged part of the old one, which the burst of frames after the switch overwrites) takes their place.

    private sealed class PresetSlot
    {
        public IReadOnlyList<Aion2SkillEntry>? Skills;
        public HashSet<int> Stigmas = new();
        public Dictionary<int, int[]> Stones = new();
        public DateTime At;
    }

    private int _activePreset = 1;
    private DateTime _presetAt = DateTime.Now;
    private bool _pruneStigmas;
    private bool _presetKnown;
    private bool _liveLists; // the game sent a skill list, stigma list or slot choices in this session (not only a restored file)
    private readonly Dictionary<int, PresetSlot> _presets = new();
    private readonly Dictionary<int, int[]> _stones = new();

    private IReadOnlyList<int>? StonesOf(int skillId) => _stones.TryGetValue(skillId, out var v) ? v : null;

    /// <summary>The number of the active skill preset (1 until the game says otherwise).</summary>
    public int ActivePreset
    {
        get
        {
            lock (_gate)
            {
                return _activePreset;
            }
        }
    }

    /// <summary>The preset the game switched to (frame 0x92e2, also sent at login).</summary>
    public void SwitchPreset(int number)
    {
        if (number is < 1 or > 9)
        {
            return;
        }

        lock (_gate)
        {
            _listsAt = DateTime.UtcNow;
            if (!_presetKnown)
            {
                _presetKnown = true;
                if (!_liveLists)
                {
                    // The first word of the game on the active preset and only a restored list from before the presets is held: it is taken to
                    // belong to that preset - the lists of the login overwrite it, it is not parked as another preset.
                    _activePreset = number;
                    return;
                }
            }

            if (number == _activePreset)
            {
                return;
            }

            // The time a preset shows is when it was last left (the active one: when it became active), so the profile upload only changes
            // with its content and not with every list the game re-sends.
            _presetAt = DateTime.Now;
            FlushPreset();
            _activePreset = number;
            if (_presets.TryGetValue(number, out PresetSlot? known))
            {
                _skills = known.Skills;
                _stigmas.Clear();
                _stigmas.UnionWith(known.Stigmas);
                _stones.Clear();
                foreach (var (id, v) in known.Stones)
                {
                    _stones[id] = v;
                }

                _presetAt = known.At;
                _pruneStigmas = false;
            }
            else
            {
                _skills = null;
                _pruneStigmas = true;
            }
        }

        NotifyCharacterChanged();
    }

    private void FlushPreset()
    {
        if (_skills is null && _stigmas.Count == 0 && _stones.Count == 0)
        {
            return;
        }

        _presets[_activePreset] = new PresetSlot { Skills = _skills, Stigmas = new HashSet<int>(_stigmas), Stones = new Dictionary<int, int[]>(_stones), At = _presetAt };
    }

    private IReadOnlyList<Aion2PresetInfo> PresetsLocked()
    {
        FlushPreset();
        return _presets.OrderBy(kv => kv.Key)
            .Where(kv => kv.Value.Skills is { Count: > 0 })
            .Select(kv => new Aion2PresetInfo(kv.Key, kv.Key == _activePreset, kv.Value.At,
                kv.Value.Skills!.Select(k => k with { Stigma = kv.Value.Stigmas.Contains(k.SkillId), Equipped = _bar.Contains(k.SkillId), Stones = kv.Value.Stones.TryGetValue(k.SkillId, out var v) ? v : null }).ToList()))
            .ToList();
    }

    /// <summary>Every preset seen so far, the active one marked; one entry when the character has no presets.</summary>
    public IReadOnlyList<Aion2PresetInfo> LocalPresets
    {
        get
        {
            lock (_gate)
            {
                return PresetsLocked();
            }
        }
    }

    /// <summary>The choices in the specialisation slots of skills (skill id to the stone numbers; 0 = empty). A stigma has
    /// five slots, an active skill three; <paramref name="stigmas"/> are the ones seen with five slots.</summary>
    public void SetLocalSpecSlots(IReadOnlyDictionary<int, int[]> slots, IReadOnlySet<int> stigmas)
    {
        lock (_gate)
        {
            _liveLists = true;
            foreach (var (id, v) in slots)
            {
                _stones[id] = v;
            }

            _stigmas.UnionWith(stigmas);
            if (_skills is not null)
            {
                _skills = _skills.Select(s => s with { Stigma = _stigmas.Contains(s.SkillId), Stones = StonesOf(s.SkillId) }).ToList();
            }
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
            // The login list names the stigmas of the active preset and no others: it replaces the set (a leftover from a restored file or
            // from the other preset would show as a fifth stigma). A frame that names none leaves the set as it was.
            _liveLists = true;
            if (ids.Count > 0)
            {
                _stigmas.Clear();
            }
            else
            {
                _stigmas.RemoveWhere(listed.Contains);
            }

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
        player = WithKnownOrigin(player);
        lock (_gate)
        {
            _inspected[player.Name] = player;
        }

        InspectedChanged?.Invoke();
    }

    /// <summary>Notes that this very window (same name, same time) was uploaded.</summary>
    public void MarkInspectedUploaded(string name, DateTime receivedAt)
    {
        lock (_gate)
        {
            if (_inspected.TryGetValue(name, out var window) && window.ReceivedAt == receivedAt)
            {
                _inspected[name] = window with { UploadedAt = DateTime.Now };
            }
        }

        InspectedChanged?.Invoke();
    }

    /// <summary>The window with the faction and the server the stream knows for the name filled in where they are still missing.</summary>
    private Aion2InspectedPlayer WithKnownOrigin(Aion2InspectedPlayer player) =>
        player.Faction is { Length: > 0 } && player.ServerId is > 0
            ? player
            : player with
            {
                Faction = player.Faction is { Length: > 0 } ? player.Faction : FactionOfName(player.Name),
                ServerId = player.ServerId is > 0 ? player.ServerId : ServerIdOfName(player.Name),
            };

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
            // A player named after his window was opened gets his faction and server now; the next save keeps them.
            foreach (var entry in _inspected.ToList())
            {
                var known = WithKnownOrigin(entry.Value);
                if (known.Faction != entry.Value.Faction || known.ServerId != entry.Value.ServerId)
                {
                    _inspected[entry.Key] = known;
                }
            }

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
            _npcLevels.Remove(entityId); // the id is reused: the level of its earlier life no longer applies
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

    // Entity id -> the monster's level as its spawn frame names it (the difficulty step of an Ascension rite boss).
    private readonly Dictionary<int, int> _npcLevels = new();

    /// <summary>Remembers the level a monster's spawn frame carried.</summary>
    public void NoteNpcLevel(int entityId, int level)
    {
        lock (_gate)
        {
            _npcLevels[entityId] = level;
        }
    }

    /// <summary>The level of a monster, when its spawn frame named one.</summary>
    public int? NpcLevelOf(int entityId)
    {
        lock (_gate)
        {
            return _npcLevels.TryGetValue(entityId, out int level) ? level : null;
        }
    }

    /// <summary>Every entity's hit points as the server reports them, and when a monster was reset
    /// to full health (a wipe and retry under the same entity id).</summary>
    public Aion2HitPoints HitPoints { get; } = new();

    private readonly Dictionary<int, DateTime> _engagedSince = new();

    /// <summary>An entity went into combat (the game's own signal 0x218d, which also fires when someone only stands too close to a boss).
    /// <paramref name="exact"/>: the signal that carries the start time; the plain "in combat" flag only marks the first time it was seen and
    /// yields to the exact one.</summary>
    public void NoteEngaged(int entity, DateTime at, bool exact = false)
    {
        lock (_gate)
        {
            if (_engagedSince.Count > 5000)
            {
                _engagedSince.Clear();
            }

            if (exact)
            {
                _engagedSince[entity] = at;
            }
            else
            {
                _engagedSince.TryAdd(entity, at);
            }
        }
    }

    /// <summary>An entity left combat (the boss was reset, died or the fight ended).</summary>
    public void NoteDisengaged(int entity)
    {
        lock (_gate)
        {
            _engagedSince.Remove(entity);
        }
    }

    /// <summary>When the entity went into combat; null when it is not in combat or the meter did not see it start.</summary>
    public DateTime? EngagedSince(int entity)
    {
        lock (_gate)
        {
            return _engagedSince.TryGetValue(entity, out DateTime at) ? at : null;
        }
    }

    /// <summary>Casts, whom entities aim at and the effects on them (see <see cref="Aion2CombatState"/>).</summary>
    public Aion2CombatState Combat { get; } = new();

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

    // Players whose first sight was in the 10 against 10 arena: the game evens everybody out there (combat power, skill levels), so nothing a client read
    // of them is real. Only name, server and legion may go online for them.
    private readonly HashSet<string> _metInEqualizedArena = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastMet = new(StringComparer.Ordinal);
    private static readonly TimeSpan EqualizedArenaLeadTime = TimeSpan.FromSeconds(15);

    /// <summary>A player's "appeared" frame came at that time. In the arena the player is marked; seen anywhere else he is unmarked again.</summary>
    public void NoteMet(string name, DateTime at)
    {
        lock (_gate)
        {
            if (_lastMet.Count > 20_000)
            {
                _lastMet.Clear(); // nothing is ever that many: a table that lost its way
            }

            _lastMet[name] = at;
            if (Aion2Zones.IsArena(_zoneId))
            {
                _metInArena.Add(name);
                _arenaUploaded.Remove(name); // met again in a new fight: sent again after it
            }

            if (Aion2Zones.IsEqualizedArena(_zoneId))
            {
                _metInEqualizedArena.Add(name);
            }
            else
            {
                _metInEqualizedArena.Remove(name);
            }
        }
    }

    /// <summary>The player entered the arena: the players announced just before the zone frame (the game sends the teams ahead of it) belong to it.</summary>
    public void NoteEqualizedArenaEntered(DateTime at)
    {
        lock (_gate)
        {
            foreach ((string name, DateTime met) in _lastMet)
            {
                if (at - met >= TimeSpan.Zero && at - met <= EqualizedArenaLeadTime)
                {
                    _metInEqualizedArena.Add(name);
                }
            }
        }
    }

    // Players met in an arena (any of them): they go online once, when the fight is over.
    private readonly HashSet<string> _metInArena = new(StringComparer.Ordinal);
    private readonly HashSet<string> _arenaUploaded = new(StringComparer.Ordinal);

    /// <summary>The player entered an arena: the players announced just before the zone frame belong to it.</summary>
    public void NoteArenaEntered(DateTime at)
    {
        lock (_gate)
        {
            foreach ((string name, DateTime met) in _lastMet)
            {
                if (at - met >= TimeSpan.Zero && at - met <= EqualizedArenaLeadTime)
                {
                    _metInArena.Add(name);
                }
            }
        }
    }

    /// <summary>The names of the players met in an arena that were not uploaded yet.</summary>
    public IReadOnlySet<string> ArenaNamesToUpload()
    {
        lock (_gate)
        {
            return _metInArena.Where(n => !_arenaUploaded.Contains(n)).ToHashSet(StringComparer.Ordinal);
        }
    }

    public void MarkArenaUploaded(IEnumerable<string> names)
    {
        lock (_gate)
        {
            foreach (string name in names)
            {
                _arenaUploaded.Add(name);
            }
        }
    }

    public bool MetInEqualizedArena(string name)
    {
        lock (_gate)
        {
            return _metInEqualizedArena.Contains(name);
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

    /// <summary>The faction of a player known by name only (a character window has no combat id): through the server he was
    /// named with, else through the combat id the game announced him under.</summary>
    public string? FactionOfName(string name)
    {
        int? id;
        lock (_gate)
        {
            if (_serverOfName.TryGetValue(name, out int server) && server / 1000 is 1 or 2)
            {
                return server / 1000 == 1 ? "Elyos" : "Asmodian";
            }

            id = _names.FirstOrDefault(kv => string.Equals(kv.Value, name, StringComparison.Ordinal)).Key is int found && found != 0 ? found : null;
        }

        return id is int known ? FactionOf(known) : null;
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

    private readonly Dictionary<string, int> _idOfName = new(StringComparer.Ordinal);
    // name -> server -> permanent character id. A name listed on two servers belongs to two players (a matched world holds both).
    private readonly Dictionary<string, Dictionary<int, int>> _characterOfName = new(StringComparer.Ordinal);

    /// <summary>The permanent character id of a player, as the frames that list players carry it (see OpcodeFamily.PlayerList). It never changes with the
    /// combat id, so it tells two players of one name apart (two servers of a matched world) and confirms that two combat ids are one player.</summary>
    public void NoteCharacterId(string name, int serverId, int characterId)
    {
        if (serverId is < 1000 or > 9999 || characterId <= 0 || name.Length < 2)
        {
            return;
        }

        lock (_gate)
        {
            if (!_characterOfName.TryGetValue(name, out var servers))
            {
                _characterOfName[name] = servers = new Dictionary<int, int>();
            }

            servers[serverId] = characterId;
            if (!_serverOfName.ContainsKey(name))
            {
                _serverOfName[name] = serverId;
            }
        }
    }

    /// <summary>The permanent character id of the player of this name on this server, when a player list named him (0 = not known).</summary>
    public int CharacterIdOf(string name, int serverId)
    {
        lock (_gate)
        {
            return _characterOfName.TryGetValue(name, out var servers) && servers.TryGetValue(serverId, out int characterId) ? characterId : 0;
        }
    }

    /// <summary>A player is one name (unique per server): when the game announces him under another combat id - a map change gives everybody new ones,
    /// and so does coming back into view - the old id is the same player and <see cref="IdentityMoved"/> says so. Called for every frame that
    /// names a combat id; <paramref name="serverId"/> is 0 where the frame does not carry it (the nickname frame). Not the same player when
    /// the servers differ, when the name is listed for two servers (two permanent character ids) and the frame does not say which, or when the two ids play other classes (a namesake of another server).</summary>
    public void NoteIdentity(int id, string name, int serverId)
    {
        if (name.Length < 2)
        {
            return;
        }

        int? moved = null;
        lock (_gate)
        {
            if (serverId is >= 1000 and <= 9999 && !_serverOfName.ContainsKey(name))
            {
                _serverOfName[name] = serverId;
            }

            if (_idOfName.TryGetValue(name, out int previous) && previous != id && !AreNamesakes(previous, id, name, serverId))
            {
                moved = previous;
                IdentityMovesSeen++;
            }

            _idOfName[name] = id;
        }

        if (moved is int old)
        {
            IdentityMoved?.Invoke(old, id);
        }
    }

    // Called under _gate: two ids that carry the same name but cannot be one player.
    private bool AreNamesakes(int previous, int id, string name, int serverId)
    {
        if (serverId is >= 1000 and <= 9999 && _serverOfName.TryGetValue(name, out int known) && known != serverId
            && !(_otherServerIds.TryGetValue(name, out var aliases) && aliases.Contains(serverId)))
        {
            return true;
        }

        // A frame without a server (the nickname frame): a name the player lists know on two servers is two players, and which one this is stays open.
        if (serverId == 0 && _characterOfName.TryGetValue(name, out var servers) && servers.Count > 1)
        {
            return true;
        }

        string? before = ClassOf(previous), now = ClassOf(id);
        return before is not null && now is not null && before != now;
    }

    public int IdentityMovesSeen { get; private set; }

    /// <summary>A player known under one combat id was announced under another (same name, same server).</summary>
    public event Action<int, int>? IdentityMoved;

    private readonly Dictionary<string, int> _serverOfName = new(StringComparer.Ordinal);

    // Players whose server came from the party roster's second id (see NoteHomeServerOfName), and the other ids the game printed in front of their name.
    private readonly HashSet<string> _homeServerKnown = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<int>> _otherServerIds = new(StringComparer.Ordinal);

    /// <summary>
    /// A player's server id as the game announced it next to his name (appearance and party frames). For a player whose home server is known from the party roster
    /// (<see cref="NoteHomeServerOfName"/>) the id is only remembered as one the game prints for him, it does not replace the home server.
    /// </summary>
    public void NoteServerOfName(string name, int serverId)
    {
        if (serverId is >= 1000 and <= 9999)
        {
            lock (_gate)
            {
                if (_homeServerKnown.Contains(name))
                {
                    if (_serverOfName.TryGetValue(name, out int home) && home != serverId)
                    {
                        NoteOtherServerId(name, serverId);
                    }

                    return;
                }

                _serverOfName[name] = serverId;
            }
        }
    }

    /// <summary>
    /// The server id the party roster prints after a member's name: the character's server as NC lists it. The id in front of the name is the same for most players but
    /// differs for some (SaintlyOrder 2306 in front, 2303 behind; NC: Triniel 2303), and NC agreed with the one behind the name for every such player checked (2026-10-17).
    /// It wins over every other source from then on; the id in front stays known as an alias so the player's frames are not taken for a namesake.
    /// </summary>
    public void NoteHomeServerOfName(string name, int serverId)
    {
        if (serverId is < 1000 or > 9999)
        {
            return;
        }

        lock (_gate)
        {
            if (_serverOfName.TryGetValue(name, out int previous) && previous != serverId)
            {
                NoteOtherServerId(name, previous);
            }

            _serverOfName[name] = serverId;
            _homeServerKnown.Add(name);
        }
    }

    // Called under _gate.
    private void NoteOtherServerId(string name, int serverId)
    {
        if (!_otherServerIds.TryGetValue(name, out var others))
        {
            _otherServerIds[name] = others = new HashSet<int>();
        }

        others.Add(serverId);
    }

    /// <summary>The server id the game announced next to a name (a player known only by his name, e.g. one whose character window was opened); null when none was seen.</summary>
    public int? ServerIdOfName(string name)
    {
        lock (_gate)
        {
            return _serverOfName.TryGetValue(name, out int server) ? server : null;
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
                // Only for the player the snapshot knew under that id; an id reused since belongs to somebody else.
                if (snapshot.Names.TryGetValue(id, out string? savedName) && _names.GetValueOrDefault(id) == savedName)
                {
                    _guilds.TryAdd(id, guild);
                }
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

    /// <summary>The legion of a named player: only when the id is known under that very name, so a legion left over from another player of the same id is never reported.</summary>
    public string? GuildOf(int id, string name)
    {
        lock (_gate)
        {
            return _names.GetValueOrDefault(id) == name ? _guilds.GetValueOrDefault(id) : null;
        }
    }

    private readonly Dictionary<string, string> _accountLegions = new(StringComparer.Ordinal);
    private string? _accountKey;

    /// <summary>The account key of the login's character list (the same on every character of one account); replaced at every login.</summary>
    public void NoteAccountKey(string key)
    {
        lock (_gate)
        {
            _accountKey = key;
        }
    }

    /// <summary>A hash of the account key of the current login (16 hex digits), or null before the character list arrived. The key itself is never sent.</summary>
    public string? AccountKeyHash()
    {
        lock (_gate)
        {
            if (_accountKey is null)
            {
                return null;
            }

            byte[] digest = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes("aiondps-account:" + _accountKey));
            return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
        }
    }

    /// <summary>A character of the own account with its legion, from the login's character list.</summary>
    public void NoteAccountLegion(string name, string legion)
    {
        lock (_gate)
        {
            _accountLegions[name] = legion;
        }
    }

    private readonly Dictionary<string, AccountCharacter> _accountCharacters = new(StringComparer.Ordinal);

    /// <summary>The characters of the own account from the login's character list (all of them, not only the one that logged in); replaced at every login.</summary>
    public void NoteAccountCharacters(IReadOnlyList<AccountCharacter> characters)
    {
        lock (_gate)
        {
            _accountCharacters.Clear();
            foreach (AccountCharacter character in characters)
            {
                _accountCharacters[character.Name] = character;
            }
        }
    }

    /// <summary>The characters of the own account as the last character list showed them, in the order of the list.</summary>
    public IReadOnlyList<AccountCharacter> AccountCharacters()
    {
        lock (_gate)
        {
            return _accountCharacters.Values.ToList();
        }
    }

    /// <summary>The legion of a character of the own account (the login's character list), or null.</summary>
    public string? AccountLegionOf(string name)
    {
        lock (_gate)
        {
            return _accountLegions.GetValueOrDefault(name);
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

    // The members the latest roster frame listed, the local player included.
    private HashSet<string> _party = new(StringComparer.Ordinal);

    /// <summary>Notes a party roster frame: it lists the group as it is now, so it replaces the previous one.
    /// A frame changes only when somebody joins or leaves, and a party breaking up ends with a frame naming the
    /// local player alone. Names used to be kept for 90 s after their last mention, so whoever left within 90 s
    /// of the last frame stayed in the group for good (found in StacysView's fork, 2026-10-17).</summary>
    public void NoteParty(IReadOnlyCollection<string> names, DateTime at)
    {
        lock (_gate)
        {
            _party = new HashSet<string>(names, StringComparer.Ordinal);
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

    /// <summary>Names in the local player's party: those of the latest roster frame. Empty when no roster has
    /// arrived yet.</summary>
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

    /// <summary>How many players the latest roster frame listed (the local player included); 0 when none has arrived.</summary>
    public int PartySize
    {
        get
        {
            lock (_gate)
            {
                return _party.Count;
            }
        }
    }

    private HashSet<string> CurrentPartyNames() => new(_party, StringComparer.Ordinal);

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
            // Two passes without sorting or allocating: this runs for every row of the list on every refresh, and
            // sorting all known players each time grew with the square of their number. The picks are the ones the
            // sorted lists gave (the first of equals comes first).
            double bestStat = double.NegativeInfinity, secondStat = double.NegativeInfinity;
            int bestStatId = 0;
            foreach (KeyValuePair<int, double> stat in _detailedStats)
            {
                if (stat.Value > bestStat)
                {
                    secondStat = bestStat;
                    bestStat = stat.Value;
                    bestStatId = stat.Key;
                }
                else if (stat.Value > secondStat)
                {
                    secondStat = stat.Value;
                }
            }

            if (_detailedStats.Count > 0 && bestStat >= 5 && (_detailedStats.Count == 1 || bestStat >= 3 * secondStat))
            {
                return bestStatId;
            }

            int unnamed = 0, bestId = 0, bestVotes = int.MinValue, secondVotes = int.MinValue;
            foreach (KeyValuePair<int, Dictionary<string, int>> entry in _classVotes)
            {
                if (_names.ContainsKey(entry.Key))
                {
                    continue;
                }

                int votes = 0;
                foreach (int count in entry.Value.Values)
                {
                    votes += count;
                }

                unnamed++;
                if (votes > bestVotes)
                {
                    secondVotes = bestVotes;
                    bestVotes = votes;
                    bestId = entry.Key;
                }
                else if (votes > secondVotes)
                {
                    secondVotes = votes;
                }
            }

            if (unnamed == 0 || (unnamed > 1 && bestVotes < 3 * secondVotes))
            {
                return null;
            }

            return bestId;
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
            // An id that now belongs to another player must not keep the legion of the one before (ids are handed out again).
            if (_names.TryGetValue(id, out string? before) && before != name)
            {
                _guilds.Remove(id);
                // The id now belongs to another player: the old name must not point to it any more.
                if (_ids.TryGetValue(before, out int ofBefore) && ofBefore == id)
                {
                    _ids.Remove(before);
                }
            }

            _names[id] = name;
            _ids[name] = id;

            // A summon announced by its owner's name (a Cleric's Divine Aura) before that name was known to belong to an id: its first hits
            // stayed on the summon's own id ("Player #id"); now the owner is found, so they are handed over (see DrainResolvedOwners).
            foreach ((int summon, string ownerName) in _summonOwnerNames)
            {
                if (ownerName == name && summon != id && !_summonOwners.ContainsKey(summon) && _resolvedOwners.Count < 4096)
                {
                    _summonOwners[summon] = id;
                    _resolvedOwners.Add((summon, id));
                }
            }
        }
    }
}
