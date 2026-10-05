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

    public static string? ItemIconPath(int itemId)
    {
        _itemIcons ??= ReadMap(Path.Combine(Root, "items", "item_icons.json"));
        if (!_itemIcons.TryGetValue(itemId, out string? name))
        {
            return null;
        }

        string path = Path.Combine(Root, "items", "icons", name + ".png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>True for passive skills ("p" in the client's skill table); everything else counts as active.</summary>
    public static bool IsPassive(int skillId)
    {
        _skillTypes ??= ReadMap(Path.Combine(Root, "skills", "skill_types.json"));
        return _skillTypes.TryGetValue(skillId, out string? type) && type == "p";
    }

    private static readonly Dictionary<string, string> ClassFiles = new()
    {
        ["Gladiator"] = "gladiator", ["Templar"] = "templar", ["Ranger"] = "ranger", ["Assassin"] = "assassin",
        ["Spiritmaster"] = "elementalist", ["Sorcerer"] = "sorcerer", ["Cleric"] = "cleric", ["Chanter"] = "chanter", ["Brawler"] = "fighter",
    };

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
