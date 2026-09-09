namespace AionSniffer.Upload;

/// <summary>One skill's usage for one participant - field-for-field what the backend's
/// uploadSchema.ts zod schema expects (skill/hits/critHits/total/min/max).</summary>
public sealed record SkillUsageUpload(string Skill, int Hits, int CritHits, long Total, long Min, long Max);

/// <summary>One player's contribution to an encounter. <see cref="IsSelf"/> mirrors the client's own
/// "You" check (see MainWindow.ResolveDisplayName) - the backend trusts THIS row's crit rate for
/// this player permanently once received, since Aion only flags crits reliably in the scorer's own
/// log (see Combat/CritEstimator.cs).</summary>
public sealed record ParticipantUpload(
    string Name,
    string ClassName,
    string Faction,
    bool IsSelf,
    long TotalDamage,
    double Dps,
    double Idps,
    long? ApTotal,
    IReadOnlyList<SkillUsageUpload> Skills);

/// <summary>One boss encounter, as sent to POST /api/uploads. The backend recognizes the same real
/// fight across several independent uploads (one per group member) by boss + time window + roster
/// overlap - see backend/src/matching/merge.ts.</summary>
public sealed record EncounterUploadRequest(
    string ClientVersion,
    string BossNpcName,
    DateTime StartedAt,
    DateTime EndedAt,
    IReadOnlyList<ParticipantUpload> Participants);
