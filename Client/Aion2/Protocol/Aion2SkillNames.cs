using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Aion 2 skill id → English name, from assets/aion2/skills/skill_names.json ({"11010000": "Cleave", …}).
/// Aion 2 skill ids are 8 digits; the last four carry level/specialisation, so a lookup falls back
/// to the base id (id rounded down to a multiple of 10000) before giving up and returning the raw
/// number - a meter that shows "12040130" is more useful than one that shows nothing.
/// </summary>
public static class Aion2SkillNames
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "skill_names.json");
    private static IReadOnlyDictionary<int, string>? _cache;

    public static IReadOnlyDictionary<int, string> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<int, string>();
        try
        {
            if (File.Exists(FilePath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new();
                foreach ((string key, string name) in raw)
                {
                    if (int.TryParse(key, out int id))
                    {
                        table[id] = name;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Missing or broken table: names degrade to numbers, the meter keeps working.
        }

        _cache = table;
        return table;
    }

    public static string NameOf(int skillId)
    {
        IReadOnlyDictionary<int, string> table = Load();
        if (table.TryGetValue(skillId, out string? exact))
        {
            return exact;
        }

        int baseId = skillId / 10000 * 10000;
        return table.TryGetValue(baseId, out string? byBase) ? byBase : skillId.ToString();
    }
}
