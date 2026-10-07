namespace AionDPS.Upload;

/// <summary>Outcome of one UploadClient.SendAsync call. <see cref="Error"/> is null exactly when
/// <see cref="Success"/> is true - carries the real reason for a failure (HTTP status + response
/// body, or the exception message) rather than collapsing every failure into one generic message.</summary>
public readonly record struct UploadResult(bool Success, string? Error)
{
    public static UploadResult Ok { get; } = new(true, null);

    public static UploadResult Failed(string error) => new(false, error);
}

/// <summary>One skill's usage for one participant - field-for-field what the backend's
/// uploadSchema.ts zod schema expects (skill/hits/critHits/total/min/max).</summary>
public sealed record SkillUsageUpload(string Skill, int Hits, int CritHits, long Total, long Min, long Max);

/// <summary>One real reinforcement this participant RECEIVED -
/// deliberately its own, narrower record rather than reusing SkillUsageUpload: a buff cast has no
/// damage/crit/min/max to report, and forcing those fields to 0 would misrepresent them as measured
/// zeros rather than "not applicable". <see cref="Casts"/> counts how many times this row was
/// affected by the skill, whether self-cast or landed on them by a Cleric/Chanter's group buff -
/// not how many times this row itself cast it.</summary>
public sealed record BuffUsageUpload(string Skill, int Casts);

/// <summary>One player's contribution to an encounter. <see cref="IsSelf"/> mirrors the client's own
/// "You" check (see MainWindow.ResolveDisplayName) - the backend trusts THIS row's crit rate for
/// this player permanently once received, since the game only flags crits reliably on the scorer's own
/// side (see Combat/CritEstimator.cs). Per the user: AP/Kinah/EXP/loot are never part of this payload
/// -- only combat performance (damage AND heal) is - so there is deliberately no ApTotal field here
/// anymore.</summary>
public sealed record ParticipantUpload(
    string Name,
    string ClassName,
    string Faction,
    bool IsSelf,
    long TotalDamage,
    double Dps,
    double Idps,
    long TotalHealing,
    double Hps,
    IReadOnlyList<SkillUsageUpload> Skills,
    IReadOnlyList<SkillUsageUpload> HealSkills,
    // How much of the BOSS's own damage output this row ate, over the same window TotalDamage was
    // computed for - the opposite direction from TotalDamage (dealt TO the boss). Per the user: the
    // web frontend's damage-distribution chart is meant to show who took the boss's hits, not who
    // hit the boss - a different question a raid needs answered (aggro/tank checks) that the
    // existing dealt-damage total cannot answer.
    long DamageTaken = 0,
    // Per the user: the web frontend's "Buffs" column must show real reinforcements, not the
    // damage/heal skills it showed before.
    IReadOnlyList<BuffUsageUpload>? Buffs = null,
    // The guild the packet stream named next to this player. Null where unknown.
    string? Guild = null,
    // The character profile (see ProfileUpload).
    ProfileUpload? Profile = null,
    // Shields this player gave, per recipient (the class that owns the shield skill, worked out from the
    // group; nothing when it is ambiguous).
    IReadOnlyList<ShieldGivenUpload>? ShieldsGiven = null);

public sealed record ShieldGivenUpload(string PlayerName, long Amount);

/// <summary>An Aion 2 character as the client read it from the game's traffic (ids only; the website
/// resolves names). Source "self" is the uploader's own character - level, full equipment with
/// enchants, skills and Daevanion; "seen" is what could be read off another player.</summary>
public sealed record ProfileUpload(
    string Source,
    int? Level,
    int? ClassId,
    int? Faction,
    IReadOnlyList<ProfileGearUpload> Gear,
    IReadOnlyList<ProfileSkillUpload> Skills,
    IReadOnlyList<ProfileBoardUpload> Daevanion,
    // Species knowledge (own character only): level, progress and analysed effects of Cognia..Specia.
    IReadOnlyList<ProfileSpeciesUpload>? Species = null,
    // The gear score ("Ausrüstungswert", the number beside the helmet icon in the game's character window); only
    // read for players whose window the uploader opened (see Aion2FrameDecoder.DecodeInspect). Null when unknown.
    // The combat power ("Kampfkraft", the big number with the crossed swords) is not read yet.
    int? GearScore = null,
    // The three worn titles (slot 1..3 and the game's title id), the pet circles (quality of each effect slot) and,
    // for other players, how many nodes of each Daevanion board they have (the board list above is the uploader's own).
    IReadOnlyList<ProfileTitleUpload>? Titles = null,
    IReadOnlyList<ProfilePetUpload>? Pets = null,
    IReadOnlyList<ProfileBoardCountUpload>? BoardCounts = null);

public sealed record ProfileTitleUpload(int Slot, int TitleId);

public sealed record ProfilePetUpload(int Species, int Level, IReadOnlyList<int> Kinds);

public sealed record ProfileBoardCountUpload(int Board, int Count);

public sealed record ProfileGearUpload(int Slot, int ItemId, int Enchant);

public sealed record ProfileSkillUpload(int Id, int Level, int BaseLevel, bool Stigma = false, bool Equipped = false);

public sealed record ProfileBoardUpload(int Board, IReadOnlyList<int> Nodes);

public sealed record ProfileSpeciesUpload(int Id, int Level, long Progress, IReadOnlyList<ProfileSpeciesEffectUpload> Effects);

public sealed record ProfileSpeciesEffectUpload(int Page, int Slot, int Stat, long Value);

/// <summary>One boss encounter, as sent to POST /api/uploads. The backend recognizes the same real
/// fight across several independent uploads (one per group member) by boss + time window + roster
/// overlap WITHIN one server - see Backend/src/matching/merge.ts. <see cref="ServerFingerprint"/> is
/// what makes "within one server" possible at all: two private servers can have completely
/// different gear/rate standards (per the user: EuroAion's gear level is nothing like this app's
/// home server's), so runs from different servers must never merge or share a leaderboard even if
/// boss name, timing and roster happen to coincide.</summary>
public sealed record EncounterUploadRequest(
    string ClientVersion,
    string BossNpcName,
    DateTime StartedAt,
    DateTime EndedAt,
    IReadOnlyList<ParticipantUpload> Participants,
    string ServerFingerprint,
    string? ServerName,
    // The backend's game token ("aion" | "aion2", see Backend/src/constants.ts). Boss names and
    // servers are only ever matched within one game there; a payload without it (older clients)
    // is treated as the legacy "aion" game.
    string Game = "aion2",
    // The game's numeric NPC id of the boss, which is unambiguous where the name is not (the same
    // boss name recurs across Aion 2 dungeons). Null when no id was seen.
    int? BossNpcId = null,
    // The boss's highest hit-point reading from the game's HP frame (a lower bound when the meter
    // joined mid-fight). Explore and conquest differ 2-3x in it, so the backend can tell them apart.
    long? BossMaxHp = null,
    // The numeric server id of the own character record and the game server's address (IP:port) the
    // meter captured from. The id is only unique within one region; the address is what tells the
    // regions apart, so the backend keeps both.
    int? ServerId = null,
    string? GameServer = null);

/// <summary>Aion 2 players without a boss fight, as sent to POST /api/uploads/profiles: the
/// character profiles the client read off the network, with no encounter and no damage attached.
/// Exactly one participant is the uploader themselves (<see cref="ParticipantUpload.IsSelf"/>).</summary>
public sealed record ProfilesUploadRequest(
    string ClientVersion,
    string ServerFingerprint,
    string? ServerName,
    IReadOnlyList<ProfileParticipantUpload> Participants,
    string Game = "aion2",
    int? ServerId = null,
    string? GameServer = null);

public sealed record ProfileParticipantUpload(
    string Name,
    string ClassName,
    string Faction,
    bool IsSelf,
    string? Guild,
    ProfileUpload Profile);
