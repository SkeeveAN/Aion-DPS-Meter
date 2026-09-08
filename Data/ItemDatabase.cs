using System.IO;
using System.Text.Json;

namespace AionSniffer.Data;

/// <summary>
/// Aion's item rarity tiers, in ascending order, named the way the server itself names them.
///
/// The names come from Origin Codex (origincdx.com), which publishes a "quality" string per item
/// for this exact server build. They used to be this project's own invention -- Hero/Legendary/
/// Ultimate for what OriginAion calls LEGEND/EPIC/MYTHIC -- which put the wrong word in front of
/// the user: an Epic drop was labelled "Legendary" in the Loot grid, and anyone pasting the
/// Discord table shared that mislabelling onward. The colors were right the whole time, only the
/// words were wrong.
///
/// Numeric values are unchanged and load-bearing: MainWindow's loot filter keeps everything
/// >= Unique, i.e. gold and above.
/// </summary>
public enum ItemGrade
{
    Junk = 0,
    Common = 1,
    Rare = 2,
    Legend = 3,
    Unique = 4,
    Epic = 5,
    Mythic = 6,
}

/// <summary>One item entry from assets/items/items_origincdx_4x.json (see assets/README.md).</summary>
public sealed record ItemInfo(int Id, string Name, ItemGrade Grade);

/// <summary>
/// Loads the item ID -> name/quality table. Exists so the Loot list can show "Premium Accessory
/// Flux" instead of a bare "152011048" -- Chat.log's "[item:ID;...]" tags only ever carry the
/// numeric id, never a readable name (confirmed by terminal_windows: no item name appears
/// anywhere near the tag in real loot lines). Grade is used by MainWindow to filter "trash" loot
/// down to Unique (Gold) and above, per the user.
///
/// Origin Codex is the source of truth: it describes the server actually being played, and it
/// carries OriginAion's own items, which a retail 4.x dump simply does not have (a real Sauro run
/// dropped "Cosmic Fragment" and "Eternity Comet", both unresolvable before). Where aioncodex
/// knows an id Origin Codex does not, that entry is kept rather than dropped -- 6.149 such ids,
/// and on the 85.343 ids both describe the two sources agree on the quality tier without a single
/// exception, so filling the gaps costs nothing in consistency. See assets/README.md for how the
/// merged file is produced.
/// </summary>
public static class ItemDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyDictionary<int, ItemInfo>? _cache;

    /// <summary>Loads and caches the table on first use. Returns an empty table (not an exception) if the data file is missing.</summary>
    public static IReadOnlyDictionary<int, ItemInfo> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "items", "items_origincdx_4x.json");
        var result = new Dictionary<int, ItemInfo>();

        if (File.Exists(path))
        {
            try
            {
                var rows = JsonSerializer.Deserialize<List<ItemRow>>(File.ReadAllText(path), JsonOptions) ?? new List<ItemRow>();
                foreach (var row in rows)
                {
                    result[row.Id] = new ItemInfo(row.Id, row.Name ?? $"Item #{row.Id}", ParseQuality(row.Quality));
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[ItemDatabase] Failed to parse {path}: {ex.Message}");
            }
        }

        _cache = result;
        return result;
    }

    /// <summary>
    /// Origin Codex's quality string -> tier. An unrecognised value falls back to Common rather
    /// than throwing: a new tier appearing in a future data refresh should cost the loot row its
    /// color, not take the whole table down with it.
    /// </summary>
    private static ItemGrade ParseQuality(string? quality) => quality?.ToUpperInvariant() switch
    {
        "JUNK" => ItemGrade.Junk,
        "COMMON" => ItemGrade.Common,
        "RARE" => ItemGrade.Rare,
        "LEGEND" => ItemGrade.Legend,
        "UNIQUE" => ItemGrade.Unique,
        "EPIC" => ItemGrade.Epic,
        "MYTHIC" => ItemGrade.Mythic,
        _ => ItemGrade.Common,
    };

    /// <summary>Convenience lookup: a name if known, a clearly-marked placeholder otherwise -- not
    /// every id seen in a real Chat.log resolves, so this must never throw or silently return an
    /// empty string.</summary>
    public static string DisplayName(int itemId) =>
        Load().TryGetValue(itemId, out var info) ? info.Name : $"Item #{itemId}";

    /// <summary>Grade if known, null for an unresolved id -- callers (see MainWindow's loot
    /// filter) must treat null as "can't tell", not as Common, since a below-Gold assumption for
    /// an unknown id could silently drop something that was actually worth keeping.</summary>
    public static ItemGrade? GradeOf(int itemId) =>
        Load().TryGetValue(itemId, out var info) ? info.Grade : null;

    private sealed class ItemRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Quality { get; set; }
    }
}
