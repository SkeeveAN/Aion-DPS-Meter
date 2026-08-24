using System.IO;
using System.Text.Json;

namespace AionSniffer.Data;

/// <summary>
/// Aion's six item rarity tiers, in ascending order. Numeric values match aioncodex.com's own
/// "item_grade_N" CSS classes (see assets/README.md) -- NOT guessed from general game knowledge,
/// confirmed against the site's own stylesheet colors (Common/Rare "#fff"/"#69e15e" are the same
/// value for grade 0 and 1, hence Common covering both here).
/// </summary>
public enum ItemGrade
{
    Common = 1,
    Rare = 2,
    Hero = 3,
    Unique = 4,
    Legendary = 5,
    Ultimate = 6,
}

/// <summary>One item entry from assets/items/items_en_4x.json (see assets/README.md for provenance).</summary>
public sealed record ItemInfo(int Id, string Name, ItemGrade Grade);

/// <summary>
/// Loads the item ID -> name/grade table collected from aioncodex.com's "/4x/" snapshot (91,492
/// entries, same source and locale-bucket reasoning as Data/SkillDatabase.cs). Exists so the Loot
/// list can show "Premium Accessory Flux" instead of a bare "152011048" -- Chat.log's
/// "[item:ID;...]" tags only ever carry the numeric id, never a readable name (confirmed by
/// terminal_windows: no item name appears anywhere near the tag in real loot lines). Grade is
/// used by MainWindow to filter "trash" loot down to Unique (Gold) and above, per the user.
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

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "items", "items_en_4x.json");
        var result = new Dictionary<int, ItemInfo>();

        if (File.Exists(path))
        {
            try
            {
                var rows = JsonSerializer.Deserialize<List<ItemRow>>(File.ReadAllText(path), JsonOptions) ?? new List<ItemRow>();
                foreach (var row in rows)
                {
                    // Source data's grade 0 ("Junk", extremely rare, only 3.4k of 91.5k items) is
                    // folded into Common -- both render as the exact same white (#fff) in
                    // aioncodex's own CSS, so there's no distinct color/tier to preserve.
                    var grade = row.Grade <= 1 ? ItemGrade.Common : (ItemGrade)row.Grade;
                    result[row.Id] = new ItemInfo(row.Id, row.Name ?? $"Item #{row.Id}", grade);
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

    /// <summary>Convenience lookup: a name if known, a clearly-marked placeholder otherwise -- not
    /// every id seen in a real Chat.log resolves (confirmed: 93.4% of real ids do), so this must
    /// never throw or silently return an empty string.</summary>
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
        public int Grade { get; set; }
    }
}
