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

/// <summary>An effect on the player during the fight: the share of the fight it was on (<see cref="UptimePercent"/>) and how many times it was put on (-1 = not known).</summary>
public sealed record CompareBuff(string Name, double UptimePercent, int Applications);

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
    int Deaths = 0,
    IReadOnlyList<CompareBuff>? Buffs = null,
    long? BossDamage = null,
    long? AddDamage = null)
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

/// <summary>One effect for each entry; <see cref="Gaps"/> is the entry's uptime minus the baseline's, in percentage points.</summary>
public sealed record CompareBuffRow(string Name, IReadOnlyList<CompareBuff?> PerEntry, IReadOnlyList<double> Gaps)
{
    public double BiggestGap => Gaps.Count == 0 ? 0 : Gaps.Max(Math.Abs);
}

public static class CompareModel
{
    /// <summary>The effects of every entry side by side, the ones that differ most from the baseline first; an effect that was on for under 1 % of the fight in every entry is left out. At most <paramref name="max"/> rows.</summary>
    public static List<CompareBuffRow> BuffTable(IReadOnlyList<CompareEntry> entries, int baseline, int max = 30)
    {
        baseline = Math.Clamp(baseline, 0, Math.Max(0, entries.Count - 1));
        var rows = new List<CompareBuffRow>();
        foreach (string name in entries.Where(e => e.Buffs is not null).SelectMany(e => e.Buffs!.Select(b => b.Name)).Distinct())
        {
            var per = entries.Select(e => e.Buffs?.FirstOrDefault(b => b.Name == name)).ToList();
            if (per.All(b => b is null || b.UptimePercent < 1))
            {
                continue;
            }

            double reference = per[baseline]?.UptimePercent ?? 0;
            rows.Add(new CompareBuffRow(name, per, per.Select((b, i) => i == baseline ? 0 : (b?.UptimePercent ?? 0) - reference).ToList()));
        }

        return rows.OrderByDescending(r => r.BiggestGap).ThenByDescending(r => r.PerEntry.Max(b => b?.UptimePercent ?? 0)).Take(max).ToList();
    }

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

        // boss against adds: only when a boss attempt is picked (the whole stay has no boss); an add is any target that is not a player
        long? bossDamage = null, addDamage = null;
        if (attempt is not null)
        {
            var inFight = session.Data.Events.Where(e => !e.IsHeal && e.SourceObjectId == playerId && e.Timestamp >= from && e.Timestamp <= to).ToList();
            bossDamage = inFight.Where(e => e.TargetObjectId == attempt.TargetId).Sum(e => e.Amount);
            addDamage = inFight.Where(e => e.TargetObjectId != attempt.TargetId && !session.Classes.ContainsKey(e.TargetObjectId)).Sum(e => e.Amount);
        }
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
            deaths,
            extras is null ? null : BuffUptimes(extras.Buffs, playerId, from, to),
            bossDamage,
            addDamage);
    }

    /// <summary>
    /// How long each effect was on the player inside the fight. An effect is on from its On (or a Renewed it was not on for) until its End, or until its duration has run out when
    /// nothing renewed it; the same effect from several casters is counted once. The log holds the ten minutes before the fight too, so an effect put on earlier counts from the start.
    /// </summary>
    public static List<CompareBuff> BuffUptimes(IReadOnlyList<BuffEvent> log, int playerId, DateTime from, DateTime to)
    {
        double seconds = (to - from).TotalSeconds;
        var result = new List<CompareBuff>();
        if (seconds <= 0)
        {
            return result;
        }

        // an End carries no skill, so the events are grouped by instance first and the skill is read from the instance's On/Renewed
        var perSkill = new Dictionary<int, (List<(DateTime Start, DateTime End)> Spans, int Applications)>();
        foreach (var instance in log.Where(b => b.Target == playerId && b.At <= to).GroupBy(b => b.Instance))
        {
            int skillId = instance.FirstOrDefault(b => b.SkillId != 0).SkillId;
            if (skillId == 0)
            {
                continue;
            }

            if (!perSkill.TryGetValue(skillId, out var entry))
            {
                entry = (new List<(DateTime, DateTime)>(), 0);
            }

            DateTime? start = null;
            DateTime end = DateTime.MaxValue;
            foreach (BuffEvent b in instance.OrderBy(b => b.At))
            {
                if (b.Kind == BuffKind.End)
                {
                    if (start is not null)
                    {
                        entry.Spans.Add((start.Value, Min(b.At, end)));
                        start = null;
                    }

                    continue;
                }

                if (start is not null && b.At >= end)
                {
                    entry.Spans.Add((start.Value, end)); // ran out on its own before this one
                    start = null;
                }

                if (start is null)
                {
                    start = b.At;
                    if (b.Kind == BuffKind.On && b.At >= from)
                    {
                        entry.Applications++;
                    }
                }

                end = b.DurationMs >= 0 ? b.At.AddMilliseconds(b.DurationMs) : DateTime.MaxValue;
            }

            if (start is not null)
            {
                entry.Spans.Add((start.Value, Min(end, to)));
            }

            perSkill[skillId] = entry;
        }

        foreach (var (skillId, (spans, applications)) in perSkill)
        {
            // clip to the fight and join the overlaps of the instances
            double covered = 0;
            DateTime reach = from;
            foreach ((DateTime s, DateTime e) in spans.Select(x => (Max(x.Start, from), Min(x.End, to))).Where(x => x.Item2 > x.Item1).OrderBy(x => x.Item1))
            {
                DateTime begin = Max(s, reach);
                if (e > begin)
                {
                    covered += (e - begin).TotalSeconds;
                    reach = e;
                }
            }

            if (covered > 0 || applications > 0)
            {
                result.Add(new CompareBuff(Aion2.Protocol.Aion2SkillNames.NameOf(skillId), Math.Min(100, 100 * covered / seconds), applications));
            }
        }

        return result.GroupBy(b => b.Name).Select(g => g.Count() == 1 ? g.First() : new CompareBuff(g.Key, g.Max(b => b.UptimePercent), g.Sum(b => b.Applications))).OrderByDescending(b => b.UptimePercent).ToList();
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

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
    public sealed record OnlinePlayer(long ParticipantId, string Name, string ClassName, long TotalDamage, int? CombatPower, IReadOnlyList<CompareBuff>? Buffs = null);

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
                p.TryGetProperty("combatPower", out JsonElement cp) && cp.ValueKind == JsonValueKind.Number ? cp.GetInt32() : null,
                ReadBuffs(p)));
        }

        return new OnlineFight(enc.GetProperty("id").GetInt64(), enc.TryGetProperty("bossName", out JsonElement bn) ? bn.GetString() ?? "" : "",
            enc.GetProperty("durationSeconds").GetDouble(), players);
    }

    /// <summary>The effects of a roster entry as the site lists them: the name in English, the share of the fight it was on (per mille) and how often it was put on.</summary>
    private static List<CompareBuff>? ReadBuffs(JsonElement player)
    {
        if (!player.TryGetProperty("buffs", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var buffs = new List<CompareBuff>();
        foreach (JsonElement b in list.EnumerateArray())
        {
            string? name = b.TryGetProperty("names", out JsonElement names) && names.ValueKind == JsonValueKind.Object && names.TryGetProperty("en", out JsonElement en)
                ? en.GetString()
                : b.TryGetProperty("name", out JsonElement plain) ? plain.GetString() : null;
            if (name is { Length: > 0 } && b.TryGetProperty("uptimePermille", out JsonElement up) && up.ValueKind == JsonValueKind.Number)
            {
                buffs.Add(new CompareBuff(name, up.GetDouble() / 10.0, b.TryGetProperty("casts", out JsonElement casts) && casts.ValueKind == JsonValueKind.Number ? casts.GetInt32() : -1));
            }
        }

        return buffs;
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
            total > 0 ? total : player.TotalDamage, skills.Sum(s => s.Hits), skills.Sum(s => s.CritHits), skills, Online: true, Buffs: player.Buffs);
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
