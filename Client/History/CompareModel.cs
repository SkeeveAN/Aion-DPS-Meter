using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.History;

/// <summary>One skill of one player in one fight, as the comparison reads it. <see cref="Uses"/> is the number of the game's cast notices of the skill (-1 = not known: an online fight
/// has none); a notice is not always one activation (see <see cref="SkillUse"/>), so the damage per use is a guide, not a count of casts.</summary>
public sealed record CompareSkill(string Skill, int SkillId, int Hits, int CritHits, long Total, long Min, long Max, int Uses)
{
    public long Average => Hits > 0 ? Total / Hits : 0;
}

/// <summary>One mark on a rotation timeline: seconds from the fight's start, the skill, and whether it is a cast notice (true) or a hit (false).</summary>
public readonly record struct CompareMark(double Seconds, string Skill, bool IsUse);

/// <summary>
/// One player in one fight, taken from a session of the meter (live, or loaded from a file) or from an online fight of aiondps.com. Everything the comparison shows is computed from
/// these, so the same table works for two local files, a local file against a public fight, or any mix.
/// </summary>
public sealed record CompareEntry(
    string Label,
    string Player,
    string ClassName,
    int? CombatPower,
    double Seconds,
    long TotalDamage,
    int Hits,
    int CritHits,
    IReadOnlyList<CompareSkill> Skills,
    bool Online,
    IReadOnlyList<CompareMark>? Marks = null,
    int Deaths = 0)
{
    /// <summary>Damage over the fight's length, pauses included (the iDPS rule: first to last moment of the fight).</summary>
    public double Dps => Seconds > 0 ? TotalDamage / Seconds : 0;

    public double CritRate => Hits > 0 ? 100.0 * CritHits / Hits : 0;
}

/// <summary>What one skill did for each entry; <see cref="Gaps"/> is the entry's damage per second of that skill minus the baseline's (0 for the baseline itself).</summary>
public sealed record CompareRow(string Skill, IReadOnlyList<CompareSkill?> PerEntry, IReadOnlyList<double> Dps, IReadOnlyList<double> Share, IReadOnlyList<double> Gaps)
{
    /// <summary>The biggest gap of any entry to the baseline, what the table is sorted by.</summary>
    public double BiggestGap => Gaps.Count == 0 ? 0 : Gaps.Max(Math.Abs);
}

public static class CompareModel
{
    /// <summary>The entry for one player of a session: the damage he dealt in the picked boss attempt (or the whole stay), by skill, with the cast notices of the extras when there are some.</summary>
    public static CompareEntry FromSession(SessionInfo session, BossAttempt? attempt, int playerId, FightExtras? extras, string label)
    {
        IReadOnlyList<DamageEvent> scoped = Ui.SessionManagerWindow.ScopedEvents(session, attempt, Ui.SessionManagerWindow.PlayerMode.Damage);
        List<DamageEvent> mine = scoped.Where(e => e.SourceObjectId == playerId && !e.IsHeal).ToList();
        DateTime from = attempt?.Start ?? (scoped.Count > 0 ? scoped.Min(e => e.Timestamp) : session.Start);
        DateTime to = attempt?.End ?? (scoped.Count > 0 ? scoped.Max(e => e.Timestamp) : session.End ?? session.Start);
        double seconds = Math.Max(0, (to - from).TotalSeconds);

        List<SkillUse> uses = extras?.Uses.Where(u => u.Actor == playerId && u.At >= from && u.At <= to).ToList() ?? new List<SkillUse>();
        var usesByName = uses.GroupBy(u => Aion2.Protocol.Aion2SkillNames.NameOf(u.SkillId)).ToDictionary(g => g.Key, g => g.Count());

        List<CompareSkill> skills = SkillBreakdown.For(mine)
            .Select(s => new CompareSkill(s.Skill, s.SkillId, s.Hits, s.CritHits, s.Total, s.Min, s.Max, extras is null ? -1 : usesByName.GetValueOrDefault(s.Skill)))
            .ToList();

        var marks = new List<CompareMark>();
        foreach (DamageEvent e in mine.Where(e => !e.IsTick))
        {
            marks.Add(new CompareMark((e.Timestamp - from).TotalSeconds, e.Skill ?? "(auto attack)", false));
        }

        foreach (SkillUse u in uses)
        {
            marks.Add(new CompareMark((u.At - from).TotalSeconds, Aion2.Protocol.Aion2SkillNames.NameOf(u.SkillId), true));
        }

        int deaths = extras?.Deaths?.Count(d => d.Entity == playerId && d.At >= from && d.At <= to) ?? 0;
        PlayerMeta? meta = session.Players.GetValueOrDefault(playerId);
        return new CompareEntry(
            label,
            session.Names.GetValueOrDefault(playerId) ?? $"#{playerId}",
            session.Classes.GetValueOrDefault(playerId) ?? "",
            meta?.CombatPower,
            seconds,
            skills.Sum(s => s.Total),
            skills.Sum(s => s.Hits),
            skills.Sum(s => s.CritHits),
            skills,
            Online: false,
            marks.OrderBy(m => m.Seconds).ToList(),
            deaths);
    }

    /// <summary>The skills of every entry side by side: one row per skill name, sorted by how far the entries are from the baseline, so the skills that make up the gap come first.</summary>
    public static List<CompareRow> Table(IReadOnlyList<CompareEntry> entries, int baseline)
    {
        if (entries.Count == 0)
        {
            return new List<CompareRow>();
        }

        baseline = Math.Clamp(baseline, 0, entries.Count - 1);
        var names = entries.SelectMany(e => e.Skills.Select(s => s.Skill)).Distinct().ToList();
        var rows = new List<CompareRow>();
        foreach (string name in names)
        {
            var per = entries.Select(e => e.Skills.FirstOrDefault(s => s.Skill == name)).ToList();
            var dps = entries.Select((e, i) => per[i] is { } s && e.Seconds > 0 ? s.Total / e.Seconds : 0).ToList();
            var share = entries.Select((e, i) => per[i] is { } s && e.TotalDamage > 0 ? 100.0 * s.Total / e.TotalDamage : 0).ToList();
            var gaps = dps.Select((d, i) => i == baseline ? 0 : d - dps[baseline]).ToList();
            rows.Add(new CompareRow(name, per, dps, share, gaps));
        }

        return rows.OrderByDescending(r => r.BiggestGap).ThenByDescending(r => r.Dps.Max()).ToList();
    }

    // ---- online fights (aiondps.com) ---------------------------------------------------------------------------------

    /// <summary>One player of an online fight, to pick from.</summary>
    public sealed record OnlinePlayer(long ParticipantId, string Name, string ClassName, long TotalDamage, int? CombatPower);

    public sealed record OnlineFight(long EncounterId, string BossName, double DurationSeconds, IReadOnlyList<OnlinePlayer> Players);

    private static readonly Regex EncounterUrl = new(@"(?:aiondps\.com)?/encounters/(?<id>\d{1,10})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) }; // no redirects: the host is fixed

    /// <summary>The fight number of an aiondps.com address (https://aiondps.com/encounters/17717, with or without the host, or the plain number); null for anything else.</summary>
    public static long? EncounterIdOf(string text)
    {
        text = text.Trim();
        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long plain) && plain > 0)
        {
            return plain;
        }

        if (text.Contains("://", StringComparison.Ordinal) && !text.StartsWith("https://aiondps.com/", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("https://www.aiondps.com/", StringComparison.OrdinalIgnoreCase))
        {
            return null; // only our own site
        }

        return EncounterUrl.Match(text) is { Success: true } m ? long.Parse(m.Groups["id"].Value, CultureInfo.InvariantCulture) : null;
    }

    public static OnlineFight ParseFight(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        JsonElement enc = root.GetProperty("encounter");
        var players = new List<OnlinePlayer>();
        foreach (JsonElement p in root.GetProperty("roster").EnumerateArray())
        {
            players.Add(new OnlinePlayer(
                p.GetProperty("participantId").GetInt64(),
                p.GetProperty("playerName").GetString() ?? "?",
                p.TryGetProperty("className", out JsonElement cn) ? cn.GetString() ?? "" : "",
                p.TryGetProperty("totalDamage", out JsonElement td) && td.ValueKind == JsonValueKind.Number ? td.GetInt64() : 0,
                p.TryGetProperty("combatPower", out JsonElement cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetInt32() : null));
        }

        return new OnlineFight(enc.GetProperty("id").GetInt64(), enc.TryGetProperty("bossName", out JsonElement bn) ? bn.GetString() ?? "" : "",
            enc.GetProperty("durationSeconds").GetDouble(), players);
    }

    public static CompareEntry ParseParticipant(string json, OnlineFight fight, OnlinePlayer player)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        var skills = new List<CompareSkill>();
        foreach (JsonElement s in doc.RootElement.GetProperty("damageSkills").EnumerateArray())
        {
            skills.Add(new CompareSkill(
                s.GetProperty("skillName").GetString() ?? "?",
                0,
                s.GetProperty("hits").GetInt32(),
                s.GetProperty("critHits").GetInt32(),
                s.GetProperty("totalDamage").GetInt64(),
                s.GetProperty("minHit").GetInt64(),
                s.GetProperty("maxHit").GetInt64(),
                -1));
        }

        long total = skills.Sum(s => s.Total);
        return new CompareEntry($"aiondps.com #{fight.EncounterId} · {fight.BossName}", player.Name, player.ClassName, player.CombatPower, fight.DurationSeconds,
            total > 0 ? total : player.TotalDamage, skills.Sum(s => s.Hits), skills.Sum(s => s.CritHits), skills, Online: true);
    }

    public static async Task<OnlineFight?> FetchFightAsync(long encounterId, CancellationToken cancel = default)
    {
        using HttpResponseMessage response = await Http.GetAsync($"{Upload.UploadClient.ApiBaseUrl}/api/encounters/{encounterId}", cancel);
        return response.IsSuccessStatusCode ? ParseFight(await response.Content.ReadAsStringAsync(cancel)) : null;
    }

    public static async Task<CompareEntry?> FetchEntryAsync(OnlineFight fight, OnlinePlayer player, CancellationToken cancel = default)
    {
        using HttpResponseMessage response = await Http.GetAsync($"{Upload.UploadClient.ApiBaseUrl}/api/participants/{player.ParticipantId}", cancel);
        return response.IsSuccessStatusCode ? ParseParticipant(await response.Content.ReadAsStringAsync(cancel), fight, player) : null;
    }
}
