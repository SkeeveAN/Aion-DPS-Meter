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
    /// buffs listed in rdps_watchlist.json (e.g. "Experienced Counterstrike" with an amount of 7).
    /// The list's target debuffs are left out: they are cast on the target and hit it, and their
    /// hits are damage. A Cleric's Chain of Torment (21,818 and 4,316) and a Templar's Taunt (3,010)
    /// are exactly what the HP check found missing on four boss captures (2026-10-02/04), each one
    /// at 100.0 % once they are counted (98.8 / 99.5 / 99.5 / 99.6 % before).</summary>
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
                        if (buff.TryGetProperty("kind", out JsonElement kind) && kind.GetString() == "target_debuff")
                        {
                            continue;
                        }

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
        "Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Spiritmaster", "Cleric", "Chanter", "Brawler",
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
        "Gladiator", "Templar", "Ranger", "Assassin", "Spiritmaster", "Sorcerer", "Cleric", "Chanter",
    };

    /// <summary>
    /// True for a summon's own attack, which only a summoned entity casts: a Spiritmaster spirit's
    /// "Fire Spirit: Leaping Slam" / "Ancient Spirit: Destruction" (the player's own casts are
    /// "Summon: ..." or have no "Spirit:" in their name), a Cleric's Divine Aura, a Sorcerer's
    /// Bittercold Wind.
    /// </summary>
    public static bool IsSummonAttack(int skillId)
    {
        string name = NameOf(skillId);
        return name is "Divine Aura" or "Bittercold Wind"
            || (name.Contains("Spirit:", StringComparison.Ordinal) && !name.StartsWith("Summon", StringComparison.Ordinal));
    }

    /// <summary>
    /// The class whose summon casts this attack, or null when it is no summon attack. A summon's
    /// class skills carry the class in their id; a spirit's basic attack does not ("Fire Spirit:
    /// Basic Attack" is 100011-100018, "Ancient Spirit: Basic Attack" 100051 and 100055), so there
    /// the spirit in its name stands for the Spiritmaster.
    /// </summary>
    public static string? SummonAttackClass(int skillId) =>
        !IsSummonAttack(skillId) ? null
        : ClassOf(skillId) ?? (NameOf(skillId).Contains("Spirit:", StringComparison.Ordinal) ? "Spiritmaster" : null);

    /// <summary>
    /// The class in a character record's class code: <c>4 * class id + faction bit</c> (Gladiator 5/6,
    /// Templar 9/10, Ranger 13/14, Assassin 17/18, Spiritmaster 21/22, Sorcerer 25/26, Cleric 29/30,
    /// Chanter 33/34). Verified on four known characters; null for anything outside that pattern.
    /// </summary>
    public static string? ClassFromCode(int classCode)
    {
        int id = classCode / 4;
        return classCode % 4 is 1 or 2 && id is >= 1 and <= 8 ? ClassById[id - 1] : null;
    }

    /// <summary>
    /// The class in a party roster member's code: <c>4 * class id + 1..4</c> (Gladiator 5-8 ...
    /// Chanter 33-36). Verified on 15 members of three Draupnir/Krao Cave rosters (2026-10-02)
    /// against the classes their skills show: Gladiator 6, Templar 10, Elementalist 21/24,
    /// Sorcerer 26/27/28, Cleric 30/32, Chanter 34.
    /// </summary>
    public static string? ClassFromRosterCode(int code)
    {
        int id = (code - 1) / 4;
        return code >= 1 && id is >= 1 and <= 8 ? ClassById[id - 1] : null;
    }

    private static readonly string LocalizedPath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "skills", "skill_names_i18n.json");
    private static IReadOnlyDictionary<int, Dictionary<string, string>>? _localized;

    /// <summary>The UI language (an ISO 639-1 code; the UI sets it) for <see cref="Display"/>. The
    /// names in the events stay English (<see cref="NameOf"/>): they are uploaded and grouped by,
    /// and "Vent glacial" from one player and "Bittercold Wind" from another would split a skill.</summary>
    public static string Language { get; set; } = "en";

    private static IReadOnlyDictionary<string, int>? _localizedIdsByName;

    /// <summary>
    /// The name to show for an English skill name (as <see cref="NameOf"/> gives it): the client's
    /// own text in the UI language when it has the skill, else the English name.
    /// </summary>
    public static string Display(string? englishName)
    {
        if (englishName is null || Language == "en")
        {
            return englishName ?? "";
        }

        if (LocalizedIdsByName().TryGetValue(englishName, out int id)
            && LoadLocalized().TryGetValue(id, out var names)
            && names.TryGetValue(Language, out string? name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return englishName;
    }

    // English name -> the lowest skill id under that name that has localized names.
    private static IReadOnlyDictionary<string, int> LocalizedIdsByName()
    {
        if (_localizedIdsByName is { } cached)
        {
            return cached;
        }

        var localized = LoadLocalized();
        var byName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((int id, string name) in Load().OrderBy(kv => kv.Key))
        {
            if (localized.ContainsKey(id))
            {
                byName.TryAdd(name, id);
            }
        }

        _localizedIdsByName = byName;
        return byName;
    }

    /// <summary>
    /// Skill id → name per game language, from assets/aion2/skills/skill_names_i18n.json
    /// ({"15280000": {"fr": "Vent glacial", ...}}): the client's own text tables, read by the
    /// upstream project's Tools/aion2-dat (SkeeveAN/Aion-DPS-Meter, MIT). 4,585 skills in en, de,
    /// fr, es, ru, ja, ko and pt; a skill missing there keeps its English name.
    /// </summary>
    private static IReadOnlyDictionary<int, Dictionary<string, string>> LoadLocalized()
    {
        if (_localized is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            if (File.Exists(LocalizedPath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(LocalizedPath)) ?? new();
                foreach ((string key, Dictionary<string, string> names) in raw)
                {
                    if (int.TryParse(key, out int id))
                    {
                        table[id] = names;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // No table: English names.
        }

        _localized = table;
        return table;
    }

    /// <summary>The English name (the data model's; see <see cref="Display"/> for the UI).</summary>
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
