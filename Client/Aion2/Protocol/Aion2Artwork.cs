using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// The pictures and small tables the character window shares with the website's player page: item icons
/// (assets/aion2/items/icons, 64 px, named by the client's icon name; item_icons.json maps an item id to it),
/// class emblems, the Daevanion board tiles, skill types (active / passive) and the species knowledge texts.
/// Everything is optional: a missing file means a plain tile or an English / id fallback, never an error.
/// </summary>
public static class Aion2Artwork
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "assets", "aion2");
    private static IReadOnlyDictionary<int, string>? _itemIcons;
    private static IReadOnlyDictionary<int, string>? _skillTypes;
    private static SpeciesTexts? _species;

    private static IReadOnlyDictionary<int, string>? _craftIcons;

    public static string? ItemIconPath(int itemId)
    {
        _itemIcons ??= ReadMap(Path.Combine(Root, "items", "item_icons.json"));
        if (_itemIcons.TryGetValue(itemId, out string? name) && Path.Combine(Root, "items", "icons", name + ".png") is string path && File.Exists(path))
        {
            return path;
        }

        // the materials, potions and the like of the crafting tool (assets/aion2/craft, built by Tools/aion2-icons/build_craft_icons.py)
        _craftIcons ??= ReadMap(Path.Combine(Root, "craft", "item_icons.json"));
        if (_craftIcons.TryGetValue(itemId, out string? craftName) && Path.Combine(Root, "craft", "icons", craftName + ".png") is string craftPath && File.Exists(craftPath))
        {
            return craftPath;
        }

        return null;
    }

    private static IReadOnlyDictionary<int, string>? _effectIcons;

    /// <summary>
    /// The icon of an effect that comes from a consumable (a scroll, a potion): the effect has no skill icon, but the item that causes it does. The table
    /// (items/effect_icons.json, effect id -> the item's icon name) was built from the item descriptions, which name the effects they cause, and from the
    /// item names where an effect is named like its item. Null when there is none.
    /// </summary>
    public static string? EffectIconPath(int effectId)
    {
        _effectIcons ??= ReadMap(Path.Combine(Root, "items", "effect_icons.json"));
        foreach (int id in new[] { effectId, effectId / 10, effectId / 100 })
        {
            if (id > 0 && _effectIcons.TryGetValue(id, out string? name) && Path.Combine(Root, "items", "icons", name + ".png") is string path && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>True for passive skills ("p" in the client's skill table); everything else counts as active.</summary>
    public static bool IsPassive(int skillId)
    {
        _skillTypes ??= ReadMap(Path.Combine(Root, "skills", "skill_types.json"));
        return _skillTypes.TryGetValue(skillId, out string? type) && type == "p";
    }

    private static Dictionary<int, string>? _abnormalVariants, _abnormalBases;

    /// <summary>
    /// True when the client lists the effect as a passive ("P" in skill_abnormal_types.json: the variant of the skill id first, then its rank base): a permanent
    /// trait of the character, not something that was put on it, so it does not belong in a list of the effects running on you.
    /// </summary>
    public static bool IsPassiveEffect(int skillId) => AbnormalType(skillId) == "P" || IsPassive(skillId);

    /// <summary>True when the client lists the effect as a buff ("B"): something helpful, never a debuff.</summary>
    public static bool IsBeneficialEffect(int skillId) => AbnormalType(skillId) == "B";

    /// <summary>True when the game lists the effect as a debuff ("D").</summary>
    public static bool IsDebuffEffect(int skillId) => AbnormalType(skillId) == "D";

    private static Dictionary<int, string>? _effectTypes;

    /// <summary>The type the game gives an effect id (effect_types.json, from the Waffle meter: see the notice next to it); an effect id is the skill id and a level digit, so the levels of a skill are asked in turn.</summary>
    private static string? EffectType(int skillId)
    {
        if (_effectTypes is null)
        {
            _effectTypes = new();
            try
            {
                string path = Path.Combine(Root, "skills", "effect_types.json");
                if (File.Exists(path))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var entry in doc.RootElement.EnumerateObject())
                    {
                        if (int.TryParse(entry.Name, out int key) && entry.Value.GetString() is { } kind)
                        {
                            _effectTypes[key] = kind;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // a missing table only means the older table decides
            }
        }

        string? found = null;
        for (int level = 0; level <= 9; level++)
        {
            if (_effectTypes.TryGetValue(skillId * 10 + level, out string? kind))
            {
                if (kind == "D")
                {
                    return "D"; // one level that debuffs is enough
                }

                found ??= kind;
            }
        }

        return found;
    }

    private static string? AbnormalType(int skillId)
    {
        if (EffectType(skillId) is { } known)
        {
            return known;
        }

        if (_abnormalVariants is null)
        {
            _abnormalVariants = new();
            _abnormalBases = new();
            try
            {
                string path = Path.Combine(Root, "skills", "skill_abnormal_types.json");
                if (File.Exists(path))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                    foreach (var (name, target) in new[] { ("variants", _abnormalVariants), ("bases", _abnormalBases) })
                    {
                        if (doc.RootElement.TryGetProperty(name, out var map))
                        {
                            foreach (var entry in map.EnumerateObject())
                            {
                                if (int.TryParse(entry.Name, out int key) && entry.Value.GetString() is { } kind)
                                {
                                    target[key] = kind;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // a missing or damaged table only means that nothing is hidden
            }
        }

        return _abnormalVariants.TryGetValue(skillId, out string? v) ? v : _abnormalBases!.TryGetValue(skillId - skillId % 10000, out string? b) ? b : null;
    }

    private static readonly Dictionary<string, string> ClassFiles = new()
    {
        ["Gladiator"] = "gladiator", ["Templar"] = "templar", ["Ranger"] = "ranger", ["Assassin"] = "assassin",
        ["Spiritmaster"] = "elementalist", ["Sorcerer"] = "sorcerer", ["Cleric"] = "cleric", ["Chanter"] = "chanter", ["Brawler"] = "fighter",
    };

    private static IReadOnlyDictionary<int, string>? _bossThumbs;

    /// <summary>A small picture of a boss (by its NPC id), when the website has a card for it (Tools/aion2-icons/build_boss_thumbs.py); null otherwise.</summary>
    public static string? BossThumbPath(int npcId)
    {
        if (_bossThumbs is null)
        {
            var map = new Dictionary<int, string>();
            try
            {
                string index = Path.Combine(Root, "bosses", "thumbs", "index.json");
                if (File.Exists(index))
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(index));
                    foreach (var entry in doc.RootElement.GetProperty("thumbs").EnumerateObject())
                    {
                        if (int.TryParse(entry.Name, out int id) && entry.Value.GetString() is { Length: > 0 } file)
                        {
                            map[id] = file;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                // no pictures: the overlay draws an emblem
            }

            _bossThumbs = map;
        }

        return _bossThumbs.TryGetValue(npcId, out string? name) && Existing(Path.Combine(Root, "bosses", "thumbs", name)) is { } path ? path : null;
    }

    public static string? ClassEmblemPath(string className) =>
        ClassFiles.TryGetValue(className, out string? file) && Existing(Path.Combine(Root, "classes", file + ".png")) is { } path ? path : null;

    /// <summary>A Daevanion board tile: "common", "rare", "legend", "unique" (plus "-off" when locked) or the start tile
    /// of a class ("start-gladiator", "start").</summary>
    public static string? BoardTilePath(string name) => Existing(Path.Combine(Root, "daevanion", "art", name + ".png"));

    public static string BoardStartTile(string className) => className switch
    {
        "Gladiator" => "start-gladiator", "Templar" => "start-templar", "Ranger" => "start-ranger", "Assassin" => "start-assassin",
        "Spiritmaster" => "start-elementalist", "Sorcerer" => "start-sorcerer", "Cleric" => "start-cleric", "Chanter" => "start-chanter",
        _ => "start",
    };

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private static IReadOnlyDictionary<int, string> ReadMap(string path)
    {
        var table = new Dictionary<int, string>();
        try
        {
            if (File.Exists(path))
            {
                foreach ((string key, string value) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new())
                {
                    if (int.TryParse(key, out int id))
                    {
                        table[id] = value;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // No table: no pictures.
        }

        return table;
    }

    // ---- species knowledge texts (assets/aion2/species/species_stats.json, built by Tools/aion2-dat/build_species.py)

    private sealed class SpeciesNameEntry
    {
        public Dictionary<string, string> Names { get; set; } = new();
    }

    private sealed class SpeciesStatEntry
    {
        public Dictionary<string, string> Names { get; set; } = new();
        public bool Percent { get; set; }
    }

    private sealed class SpeciesTexts
    {
        public Dictionary<string, SpeciesNameEntry> Species { get; set; } = new();
        public Dictionary<string, SpeciesStatEntry> Stats { get; set; } = new();
    }

    private static SpeciesTexts Texts()
    {
        if (_species is { } cached)
        {
            return cached;
        }

        var texts = new SpeciesTexts();
        try
        {
            string path = Path.Combine(Root, "species", "species_stats.json");
            if (File.Exists(path))
            {
                texts = JsonSerializer.Deserialize<SpeciesTexts>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? texts;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // No table: ids instead of names.
        }

        _species = texts;
        return texts;
    }

    /// <summary>The name of a species (2 Cognia .. 6 Specia) in a language, falling back to English.</summary>
    public static string SpeciesName(int speciesId, string language)
    {
        Texts().Species.TryGetValue(speciesId.ToString(), out SpeciesNameEntry? entry);
        return entry?.Names.GetValueOrDefault(language) ?? entry?.Names.GetValueOrDefault("en") ?? $"#{speciesId}";
    }

    /// <summary>A species effect's stat name in a language and whether its value is in hundredths of a percent.</summary>
    public static (string Name, bool Percent) SpeciesStat(int statId, string language)
    {
        if (!Texts().Stats.TryGetValue(statId.ToString(), out SpeciesStatEntry? entry))
        {
            return ($"Stat {statId}", false);
        }

        return (entry.Names.GetValueOrDefault(language) ?? entry.Names.GetValueOrDefault("en") ?? $"Stat {statId}", entry.Percent);
    }
}
