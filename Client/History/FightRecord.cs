using AionDPS.Combat;

namespace AionDPS.History;

public enum UploadState
{
    NotUploaded,
    Uploaded,
    Unknown,
}

/// <summary>One row of the history list.</summary>
public sealed record FightSummary(
    long Id,
    string Game,
    string? ServerName,
    DateTime StartedAt,
    DateTime EndedAt,
    string TargetName,
    string Kind,
    long TotalDamage,
    int ParticipantCount,
    string? SelfName,
    string? UploadedRaw = null)
{
    /// <summary>NULL = a boss fight not uploaded (yet); "unknown" = recorded before the history kept track; else the time it was uploaded.</summary>
    public UploadState Upload => UploadedRaw is null ? UploadState.NotUploaded : UploadedRaw == "unknown" ? UploadState.Unknown : UploadState.Uploaded;

    public bool IsBoss => string.Equals(Kind, "boss", StringComparison.Ordinal);

    public DateTime? UploadedAt => Upload == UploadState.Uploaded && DateTime.TryParse(UploadedRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime at) ? at : null;

    /// <summary>The history list's "Uploaded" column: the time, "not uploaded" for a boss fight, nothing for other fights and for those from before the history kept track.</summary>
    public string UploadDisplay => Upload switch
    {
        UploadState.Uploaded => "✓ " + (UploadedAt?.ToString("g") ?? ""),
        UploadState.NotUploaded when IsBoss => "–",
        UploadState.Unknown => "?",
        _ => "",
    };

    public TimeSpan Duration => EndedAt - StartedAt;

    public string DurationDisplay => $"{(int)Duration.TotalMinutes}:{Duration.Seconds:D2}";
}

public sealed record FightParticipant(
    string Name,
    string ClassName,
    string Faction,
    bool IsSelf,
    bool IsEnemy,
    long Damage,
    double? Dps,
    long Healing,
    long DamageTaken)
{
    public string DpsDisplay => Dps is double d ? d.ToString("F0") : "n/a";
}

/// <summary>A stored fight with everything needed to show it again: the raw events plus the
/// id→name table they were recorded under (ids are per-session, so a replay has to remap them).</summary>
public sealed record FightDetail(
    FightSummary Summary,
    IReadOnlyList<FightParticipant> Participants,
    IReadOnlyList<DamageEvent> Events,
    IReadOnlyDictionary<int, string> Names,
    FightExtras? Extras = null);
