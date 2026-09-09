namespace AionSniffer.Upload;

/// <summary>One skill's usage for one participant - field-for-field what the backend's
/// uploadSchema.ts zod schema expects (skill/hits/critHits/total/min/max).</summary>
public sealed record SkillUsageUpload(string Skill, int Hits, int CritHits, long Total, long Min, long Max);

/// <summary>One player's contribution to an encounter. <see cref="IsSelf"/> mirrors the client's own
/// "You" check (see MainWindow.ResolveDisplayName) - the backend trusts THIS row's crit rate for
/// this player permanently once received, since Aion only flags crits reliably in the scorer's own
/// log (see Combat/CritEstimator.cs). Per the user: AP/Kinah/EXP/loot are never part of this payload
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
    IReadOnlyList<SkillUsageUpload> HealSkills);

/// <summary>One boss encounter, as sent to POST /api/uploads. The backend recognizes the same real
/// fight across several independent uploads (one per group member) by boss + time window + roster
/// overlap WITHIN one server - see backend/src/matching/merge.ts. <see cref="ServerFingerprint"/> is
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
    string? ServerName);
