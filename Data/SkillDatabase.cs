using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionSniffer.Data;

/// <summary>One skill entry from assets/skills/skills_multilang_4x.json (see assets/README.md for
/// provenance/caveats). <see cref="Name"/> is the aioncodex.com (English) name the Class/Icon/Slot
/// data is keyed on; <see cref="NameDe"/> and <see cref="NameFr"/> are the same skill's name as the
/// OriginAion client itself renders it in German/French Chat.log lines, null for the 4x snapshot's
/// small number of skills that didn't textually match anything in the client's own string table.</summary>
public sealed record SkillInfo(int Id, string Name, string? Icon, string Class, string Slot, int[] Levels, string? NameDe = null, string? NameFr = null);

/// <summary>
/// Loads the skill ID -> name/class/icon table collected from aioncodex.com, extended with the
/// client's own German/French names (see assets/README.md) -- good enough to turn a raw `skillId`
/// from SM_ATTACK_STATUS/SM_ATTACK into a readable name for the calibration dump, and to recognise
/// a skill mentioned by name in a Chat.log line regardless of which of the three languages it's in.
/// Not independently verified as exact 4.6 data for the Class/Icon/Slot columns (see assets/README.md).
/// </summary>
public static class SkillDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Strips a trailing skill-rank numeral ("Ferocious Strike VI" -> "Ferocious Strike") so
    /// a chat-log skill mention matches regardless of which rank was actually used -- see
    /// <see cref="FindByLocalizedName"/>. Found necessary by terminal_windows against a real ~44k-line
    /// session: the database only carries the rank-I name for 905 of 931 (English) skills, but the log
    /// always names the rank actually cast -- an exact-string match therefore missed 84% of real "You
    /// used X" mentions (e.g. "Rupture IV"/"Robust Blow VI"/"Cleave IV" never matching their
    /// rank-I-only DB rows).</summary>
    private static readonly Regex RankSuffix = new(@"\s+[IVXLCDM]+$");

    private static IReadOnlyDictionary<int, SkillInfo>? _cache;

    /// <summary>Loads and caches the table on first use. Returns an empty table (not an exception) if the data file is missing.</summary>
    public static IReadOnlyDictionary<int, SkillInfo> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "skills", "skills_multilang_4x.json");
        var result = new Dictionary<int, SkillInfo>();

        if (File.Exists(path))
        {
            try
            {
                var rows = JsonSerializer.Deserialize<List<SkillRow>>(File.ReadAllText(path), JsonOptions) ?? new List<SkillRow>();
                foreach (var row in rows)
                {
                    result[row.Id] = new SkillInfo(row.Id, row.Name ?? $"Skill #{row.Id}", row.Icon, row.Class ?? "", row.Slot ?? "", row.Levels ?? Array.Empty<int>(), row.De, row.Fr);
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[SkillDatabase] Failed to parse {path}: {ex.Message}");
            }
        }

        _cache = result;
        return result;
    }

    /// <summary>Convenience lookup for the calibration dump: a name if known, a clearly-marked placeholder otherwise.</summary>
    public static string DisplayName(int skillId) =>
        Load().TryGetValue(skillId, out var info) ? info.Name : $"Skill #{skillId} (unknown)";

    /// <summary>
    /// Finds a skill by the name a Chat.log line actually used -- English, German or French, in
    /// whichever the log happens to be. Exact match first, rank-normalized fallback only on a miss,
    /// checked across all three languages at each stage before falling back to the weaker one:
    /// found necessary by terminal_windows for the English case (an exact-rank entry can be
    /// UNAMBIGUOUS, e.g. "Blessing of Health II" -> Chanter only, while the rank-I entry a
    /// normalized lookup would otherwise land on is ambiguous, "Cleric, Chanter") and the same
    /// reasoning applies per language: a German exact match must not lose to an English normalized
    /// one just because English happened to be checked first.
    /// </summary>
    public static SkillInfo? FindByLocalizedName(string skillName)
    {
        var all = Load().Values;
        var exact = all.FirstOrDefault(s => s.Name == skillName || s.NameDe == skillName || s.NameFr == skillName);
        if (exact is not null)
        {
            return exact;
        }

        string baseName = RankSuffix.Replace(skillName, "");
        return all.FirstOrDefault(s =>
            RankSuffix.Replace(s.Name, "") == baseName ||
            (s.NameDe is not null && RankSuffix.Replace(s.NameDe, "") == baseName) ||
            (s.NameFr is not null && RankSuffix.Replace(s.NameFr, "") == baseName));
    }

    private sealed class SkillRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public string? Class { get; set; }
        public string? Slot { get; set; }
        public int[]? Levels { get; set; }
        public string? De { get; set; }
        public string? Fr { get; set; }
    }
}
