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

    private static readonly string WatchlistPath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "rdps_watchlist.json");
    private static IReadOnlySet<int>? _buffBases;

    /// <summary>True for ids that show up in damage frames but are not damage: consumable uses
    /// (item ids 2,000,000-2,999,999, e.g. "Life Potion" with an amount of 1) and the passive/party
    /// buffs listed in rdps_watchlist.json (e.g. "Experienced Counterstrike" with an amount of 7).</summary>
    public static bool IsNonDamageEffect(int skillId)
    {
        if (skillId is >= 2_000_000 and < 3_000_000)
        {
            return true;
        }

        if (_buffBases is null)
        {
            var set = new HashSet<int>();
            try
            {
                if (File.Exists(WatchlistPath))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(WatchlistPath));
                    foreach (JsonElement buff in doc.RootElement.GetProperty("buffs").EnumerateArray())
                    {
                        set.Add(buff.GetProperty("skillId").GetInt32() / 10000 * 10000);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // No watchlist: only the consumable range is filtered.
            }

            _buffBases = set;
        }

        return _buffBases.Contains(skillId / 10000 * 10000);
    }

    private static readonly string HealFamiliesPath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "heal_skill_families.json");
    private static IReadOnlySet<int>? _healBases;

    /// <summary>Whether the skill belongs to a heal family (assets/aion2/skills/heal_skill_families.json,
    /// keyed by base id = id rounded down to a multiple of 10000, like <see cref="NameOf"/>).</summary>
    public static bool IsHealFamily(int skillId)
    {
        if (_healBases is null)
        {
            var set = new HashSet<int>();
            try
            {
                if (File.Exists(HealFamiliesPath))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(HealFamiliesPath));
                    if (doc.RootElement.TryGetProperty("heal", out JsonElement heal) && heal.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement e in heal.EnumerateArray())
                        {
                            if (e.TryGetInt32(out int id))
                            {
                                set.Add(id);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // No table: nothing is classified as a heal, everything stays damage.
            }

            _healBases = set;
        }

        return _healBases.Contains(skillId / 10000 * 10000);
    }

    private static readonly string[] ClassByPrefix =
    {
        "Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Elementalist", "Cleric", "Chanter", "Brawler",
    };

    /// <summary>The class a player skill belongs to: its first two digits (11 = Gladiator ... 19 =
    /// Brawler, the order AionFlex's skills.json lists them in and the captured skills confirm).
    /// Null for ids outside the class ranges (NPC attacks, effects).</summary>
    public static string? ClassOf(int skillId)
    {
        int prefix = skillId / 1000000;
        return skillId >= 10000000 && prefix is >= 11 and <= 19 ? ClassByPrefix[prefix - 11] : null;
    }

    private static readonly string[] ClassById =
    {
        "Gladiator", "Templar", "Ranger", "Assassin", "Elementalist", "Sorcerer", "Cleric", "Chanter",
    };

    /// <summary>
    /// The class in a character record's class code: <c>4 * class id + faction bit</c> (Gladiator 5/6,
    /// Templar 9/10, Ranger 13/14, Assassin 17/18, Elementalist 21/22, Sorcerer 25/26, Cleric 29/30,
    /// Chanter 33/34). Verified on four known characters; null for anything outside that pattern.
    /// </summary>
    public static string? ClassFromCode(int classCode)
    {
        int id = classCode / 4;
        return classCode % 4 is 1 or 2 && id is >= 1 and <= 8 ? ClassById[id - 1] : null;
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
