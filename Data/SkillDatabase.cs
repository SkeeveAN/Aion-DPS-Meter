using System.IO;
using System.Text.Json;

namespace AionSniffer.Data;

/// <summary>One skill entry from assets/skills/skills_en_4x.json (see assets/README.md for provenance/caveats).</summary>
public sealed record SkillInfo(int Id, string Name, string? Icon, string Class, string Slot, int[] Levels);

/// <summary>
/// Loads the skill ID -> name/icon table collected from aioncodex.com. English only, and not
/// independently verified as exact 4.6 data (see assets/README.md) -- good enough to turn a raw
/// `skillId` from SM_ATTACK_STATUS/SM_ATTACK into a readable name for the calibration dump and,
/// later, the UI.
/// </summary>
public static class SkillDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyDictionary<int, SkillInfo>? _cache;

    /// <summary>Loads and caches the table on first use. Returns an empty table (not an exception) if the data file is missing.</summary>
    public static IReadOnlyDictionary<int, SkillInfo> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "skills", "skills_en_4x.json");
        var result = new Dictionary<int, SkillInfo>();

        if (File.Exists(path))
        {
            try
            {
                var rows = JsonSerializer.Deserialize<List<SkillRow>>(File.ReadAllText(path), JsonOptions) ?? new List<SkillRow>();
                foreach (var row in rows)
                {
                    result[row.Id] = new SkillInfo(row.Id, row.Name ?? $"Skill #{row.Id}", row.Icon, row.Class ?? "", row.Slot ?? "", row.Levels ?? Array.Empty<int>());
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

    private sealed class SkillRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; }
        public string? Class { get; set; }
        public string? Slot { get; set; }
        public int[]? Levels { get; set; }
    }
}
