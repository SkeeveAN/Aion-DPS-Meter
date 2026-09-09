namespace AionSniffer.Upload;

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
    IReadOnlyList<SkillUsageUpload> HealSkills,
    // How much of the BOSS's own damage output this row ate, over the same window TotalDamage was
    // computed for - the opposite direction from TotalDamage (dealt TO the boss). Per the user: the
    // web frontend's damage-distribution chart is meant to show who took the boss's hits, not who
    // hit the boss - a different question a raid needs answered (aggro/tank checks) that the
    // existing dealt-damage total cannot answer.
    long DamageTaken = 0);

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
