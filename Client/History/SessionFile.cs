using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.History;

/// <summary>
/// "Save Session"/"Load Session" (App menu) - a standalone, portable snapshot of the CURRENT live
/// session (every damage/avoid/kill event plus personal stat totals) as one file the user can
/// keep or hand off, independent of the live capture (which keeps changing and isn't itself a
/// stable "this is what I was looking at" artifact). Deliberately separate from FightStore/
/// FightHistory: that's an automatic per-FIGHT archive the meter keeps for itself; this is a
/// manual, whole-SESSION export the user explicitly asks for. Loot isn't included yet - a
/// separate INotifyPropertyChanged row type (LootRow), not a plain serializable record like
/// everything below, so it was left out of this first version rather than complicating it.
/// </summary>
public static class SessionFile
{
    public const string Extension = ".aiondps";

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter", "Sessions");

    private sealed record EventDto(long T, int K, int S, int G, long A, bool H, string? Sk, bool C,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? F = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? N = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? M = null,
        // I the game's numeric skill id (Sk is only its name), D 1 = a damage-over-time tick, E 1 = the amount is a guess (the skill's average hit), not the game's number
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? I = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? D = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? E = null);

    private sealed record AvoidDto(long T, int K, int S, int G, int Kind, string? Sk);

    private sealed record KillDto(long T, int K, int? Killer, int Victim, bool VictimIsPlayer);

    /// <summary>What makes a file one stay (a SessionInfo) and not a whole meter session: where and when, the shields, who was which class.</summary>
    private sealed record HpDto(long Highest, List<long> At, List<long> Hp, List<long> Resets);

    private sealed record RageDto(long At, int Entity, long Engaged);

    private sealed record StayDto(int ZoneId, int Kind, long Start, long? End, List<EventDto> Shields, Dictionary<string, string> Classes,
        Dictionary<string, HpDto>? Hp = null, List<RageDto>? Rages = null, string? Extras = null);

    /// <summary>A player's permanent character id (C), server id (S) and combat power (P); a field the capture did not know is left out.</summary>
    private sealed record PlayerDto(
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? C,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? S,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? P);

    private sealed record EncounterDto(int Target, string Name, long Start, long End, long Damage, bool Killed);

    /// <summary>
    /// What a reader needs to tell a gap in the data from a stretch without activity (see the notes), and the setup the fight was played with. Streams = how many entries each
    /// kind of record holds (a kind that is missing here or 0 was not recorded or did not happen); Capture = the change of the capture's loss counters over the stay.
    /// </summary>
    private sealed record MetaDto(
        int Format,
        string Client,
        string Decoder,
        Dictionary<string, int> Streams,
        List<string> Notes,
        CaptureCounters? Capture = null,
        List<EncounterDto>? Encounters = null,
        BuildSnapshot? BuildStart = null,
        BuildSnapshot? BuildEnd = null,
        Dictionary<string, Aion2.Aion2SeenProfile>? SeenProfiles = null);

    private sealed record Payload(
        int Version,
        DateTime SavedAt,
        List<EventDto> Events,
        List<AvoidDto> Avoids,
        List<KillDto> Kills,
        Dictionary<string, string> Names,
        Aion2.Aion2DirectorySnapshot? Directory = null,
        StayDto? Stay = null,
        Dictionary<string, PlayerDto>? Players = null,
        MetaDto? Meta = null);

    /// <summary>What the file cannot say for sure, written into every file so a reader does not have to know the client's history.</summary>
    private static readonly string[] Notes =
    {
        "skill uses (extras.U) are the game's cast notices: a repeated effect, a multi-hit or a channelled skill also sends one, so a notice is not always one activation. Each keeps the notice's counter byte (Q), aim flags (M) and target (G) as sent; the number of real activations is uncertain",
        "events: I = the game's numeric skill id, D = 1 a damage-over-time tick (no hit, no crit), E = 1 the amount is an estimate (the skill's average hit) because the server sent no figure for that player; M = 1 a summon's hit counted for its owner",
        "avoids (Avoids): the Aion 2 stream has no record of an attack that was avoided; none was found in the recordings checked (no hit frame without damage). A parried hit is a hit with the Parry flag (F bit 1), not a separate event. The list is empty unless a dodge was seen",
        "kills: the Aion 2 stream has no kill record; a death is in extras.D (deaths)",
        "ids: damage events, avoids and extras of one player are put under one id when the game changes it during a stay; the permanent id is Players.C / Players.S (character and server)",
        "cooldowns (extras.L): the local player's skills going on cooldown (R false = used) and coming off it (R true), read from the game's cooldown list, which comes about every 0.6 s: every moment is only right to about that, and only the own character's list is sent. The game's own count of the time left is not in seconds, so only the moments are kept. Figures made from it (activations by cooldown, cooldown length, waiting time) are approximate",
        "capture: Capture holds, over the stay, how many cast notices of players and of monsters, effect frames and cooldown lists the decoder read next to the loss counters; a log with hits but none of these was recorded without them (the capture never saw those frames), not from a stay without activity",
        "build: BuildStart/BuildEnd are the own character's setup read at the start and end of the stay (TakenAt), ListsAt = when the game last sent one of the lists; they differ when the setup was changed during the stay. Other players: SeenProfiles holds what their 'player appeared' frame showed (look, visible gear)",
    };

    private static MetaDto MakeMeta(Dictionary<string, int> streams, CaptureCounters? capture, BuildSnapshot? buildStart, BuildSnapshot? buildEnd,
        List<EncounterDto>? encounters = null, IReadOnlyDictionary<int, Aion2.Aion2SeenProfile>? seen = null) =>
        new(3, Update.AppVersion.Text, "aion2", streams, Notes.ToList(), capture, encounters is { Count: > 0 } ? encounters : null, buildStart, buildEnd,
            seen is { Count: > 0 } ? seen.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value) : null);

    private static Dictionary<string, int> StreamCounts(int events, int shields, int avoids, int kills, Combat.FightExtras? extras) => new()
    {
        ["events"] = events,
        ["shields"] = shields,
        ["avoids"] = avoids,
        ["kills"] = kills,
        ["skillUses"] = extras?.Uses.Count ?? 0,
        ["buffs"] = extras?.Buffs.Count ?? 0,
        ["staggers"] = extras?.Staggers.Count ?? 0,
        ["positionTracks"] = extras?.Tracks?.Count ?? 0,
        ["windups"] = extras?.Windups?.Count ?? 0,
        ["deaths"] = extras?.Deaths?.Count ?? 0,
        ["hpSeries"] = extras?.Hp?.Count ?? 0,
        ["targetChanges"] = extras?.Targets?.Count ?? 0,
        ["cooldownMarks"] = extras?.Cooldowns?.Count ?? 0,
    };

    private static Dictionary<string, PlayerDto>? ToDto(IReadOnlyDictionary<int, PlayerMeta>? players) =>
        players is { Count: > 0 }
            ? players.ToDictionary(kv => kv.Key.ToString(), kv => new PlayerDto(kv.Value.CharacterId, kv.Value.ServerId, kv.Value.CombatPower))
            : null;

    private static EventDto ToDto(DamageEvent e) => new(e.Timestamp.Ticks, (int)e.Timestamp.Kind, e.SourceObjectId, e.TargetObjectId, e.Amount, e.IsHeal, e.Skill, e.IsCritical,
        e.Kinds is { } kinds ? (int)kinds : null, e.HitCount > 1 ? e.HitCount : null, e.ViaSummon ? 1 : null,
        e.SkillId != 0 ? e.SkillId : null, e.IsTick ? 1 : null, e.Estimated ? 1 : null);

    private static DamageEvent FromDto(EventDto d) => new(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, d.A, d.H, d.Sk, d.C,
        IsTick: d.D == 1, SkillId: d.I ?? 0, Kinds: d.F is int f ? (HitKinds)f : null, HitCount: d.N ?? 1, Estimated: d.E == 1, ViaSummon: d.M == 1);

    private static AvoidDto ToDto(AvoidEvent a) => new(a.Timestamp.Ticks, (int)a.Timestamp.Kind, a.SourceObjectId, a.TargetObjectId, (int)a.Kind, a.Skill);

    /// <summary>Writes one stay (from the session manager) as a file the same Load can read back; the extra part is what a stay needs.</summary>
    public static void SaveStay(string path, SessionInfo stay, Combat.FightExtras? extras = null)
    {
        // the boss attempts of the stay (where one fight ends and the next begins) are what a reader would otherwise have to guess from the events
        List<EncounterDto>? encounters = null;
        try
        {
            encounters = SessionAnalyzer.Analyze(stay).Attempts
                .Select(a => new EncounterDto(a.TargetId, a.Name, a.Start.Ticks, a.End.Ticks, a.Counted, a.Killed)).ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            // an analysis that cannot be made leaves the list out; the events are what matters
        }

        CaptureCounters? capture = stay.CaptureStart is { } from && stay.CaptureEnd is { } to ? to.Since(from) : null;
        var payload = new Payload(
            2,
            DateTime.Now,
            stay.Data.Events.Select(ToDto).ToList(),
            stay.Avoids.Select(ToDto).ToList(),
            new List<KillDto>(),
            stay.Names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            null,
            new StayDto(
                stay.ZoneId,
                (int)stay.Kind,
                stay.Start.Ticks,
                stay.End?.Ticks,
                stay.Data.Shields.Select(ToDto).ToList(),
                stay.Classes.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                stay.Hp.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => new HpDto(kv.Value.Highest, kv.Value.Samples.Select(x => x.At.Ticks).ToList(), kv.Value.Samples.Select(x => x.Hp).ToList(),
                        kv.Value.Resets.Select(r => r.Ticks).ToList())),
                stay.Enrages.Select(m => new RageDto(m.Timestamp.Ticks, m.EntityId, m.EngagedAt.Ticks)).ToList(),
                extras is { IsEmpty: false } ? Convert.ToBase64String(ExtrasBlob.Pack(extras)) : null),
            ToDto(stay.Players),
            MakeMeta(StreamCounts(stay.Data.Events.Count, stay.Data.Shields.Count, stay.Avoids.Count, 0, extras), capture, stay.BuildStart, stay.BuildEnd, encounters, stay.SeenProfiles));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        JsonSerializer.Serialize(gzip, payload);
    }

    /// <summary>The folder the automatically saved sessions go to when the player has picked none: the subfolder Auto of the Sessions folder.</summary>
    public static string DefaultAutoFolder => Path.Combine(DefaultDirectory, "Auto");

    /// <summary>
    /// Writes a stay that has just ended to <paramref name="folder"/> (empty = <see cref="DefaultAutoFolder"/>): the file name says the map and the start, so a stay that is
    /// taken up and ended once more is written to the same file again. Returns the path written.
    /// </summary>
    public static string AutoSave(SessionInfo stay, string folder, Combat.FightExtras? extras)
    {
        string name = string.Concat($"{Ui.SessionLabels.NameOf(stay)} {stay.Start:yyyy-MM-dd HH-mm}".Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string path = Path.Combine(folder.Length > 0 ? folder : DefaultAutoFolder, name + Extension);
        SaveStay(path, stay, extras);
        return path;
    }

    /// <summary>The stay part of a file written by <see cref="SaveStay"/>.</summary>
    public sealed record StoredStay(int ZoneId, SessionKind Kind, DateTime Start, DateTime? End, List<DamageEvent> Shields, Dictionary<int, string> Classes,
        Dictionary<int, TargetHp> Hp, List<EnrageMark> Enrages, Combat.FightExtras? Extras = null);

    public static void Save(
        string path,
        IReadOnlyList<DamageEvent> events,
        IReadOnlyList<AvoidEvent> avoids,
        IReadOnlyList<KillEvent> kills,
        IReadOnlyDictionary<int, string> names,
        Aion2.Aion2DirectorySnapshot? directory = null,
        IReadOnlyDictionary<int, PlayerMeta>? players = null,
        SessionMeta? meta = null,
        Combat.FightExtras? extras = null)
    {
        var payload = new Payload(
            1,
            DateTime.Now,
            events.Select(ToDto).ToList(),
            avoids.Select(ToDto).ToList(),
            kills.Select(k => new KillDto(k.Timestamp.Ticks, (int)k.Timestamp.Kind, k.KillerObjectId, k.VictimObjectId, k.VictimIsPlayer)).ToList(),
            names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            directory,
            Players: ToDto(players),
            Meta: MakeMeta(StreamCounts(events.Count, 0, avoids.Count, kills.Count, extras), meta?.Capture, meta?.Build, meta?.Build));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream output = File.Create(path);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        JsonSerializer.Serialize(gzip, payload);
    }

    public sealed record LoadedSession(
        List<DamageEvent> Events,
        List<AvoidEvent> Avoids,
        List<KillEvent> Kills,
        Dictionary<int, string> Names,
        DateTime SavedAt,
        Aion2.Aion2DirectorySnapshot? Directory = null,
        StoredStay? Stay = null,
        Dictionary<int, PlayerMeta>? Players = null);

    public static LoadedSession Load(string path)
    {
        using FileStream input = File.OpenRead(path);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        Payload payload = JsonSerializer.Deserialize<Payload>(gzip)
            ?? throw new InvalidDataException("Not a valid Aion DPS session file.");

        var events = payload.Events
            .Select(FromDto)
            .ToList();
        var avoids = payload.Avoids
            .Select(d => new AvoidEvent(new DateTime(d.T, (DateTimeKind)d.K), d.S, d.G, (AvoidKind)d.Kind, d.Sk))
            .ToList();
        var kills = payload.Kills
            .Select(d => new KillEvent(new DateTime(d.T, (DateTimeKind)d.K), d.Killer, d.Victim, d.VictimIsPlayer))
            .ToList();
        var names = payload.Names.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);

        StoredStay? stay = payload.Stay is { } s
            ? new StoredStay(
                s.ZoneId,
                (SessionKind)s.Kind,
                new DateTime(s.Start),
                s.End is long end ? new DateTime(end) : null,
                s.Shields.Select(FromDto).ToList(),
                s.Classes.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value),
                (s.Hp ?? new Dictionary<string, HpDto>()).ToDictionary(
                    kv => int.Parse(kv.Key),
                    kv => new TargetHp(
                        kv.Value.Highest,
                        kv.Value.At.Zip(kv.Value.Hp, (at, hp) => new Aion2.HpSample(new DateTime(at), hp)).ToList(),
                        kv.Value.Resets.Select(r => new DateTime(r)).ToList())),
                (s.Rages ?? new List<RageDto>()).Select(r => new EnrageMark(new DateTime(r.At), r.Entity, new DateTime(r.Engaged))).ToList(),
                s.Extras is { Length: > 0 } packed ? ExtrasBlob.Unpack(Convert.FromBase64String(packed)) : null)
            : null;
        var players = payload.Players?.ToDictionary(kv => int.Parse(kv.Key), kv => new PlayerMeta(kv.Value.C, kv.Value.S, kv.Value.P));
        return new LoadedSession(events, avoids, kills, names, payload.SavedAt, payload.Directory, stay, players);
    }
}
