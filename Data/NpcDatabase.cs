using System.IO;
using System.Text.Json;

namespace AionSniffer.Data;

/// <summary>
/// Known NPC/monster names collected from aioncodex.com's "/4x/" snapshot (38,090 entries, same
/// source and locale-bucket reasoning as Data/SkillDatabase.cs and Data/ItemDatabase.cs). Exists
/// to fix a real gap in the "players only" name filter (see Ui/MainWindow.xaml.cs): that filter's
/// "no space in the name" heuristic assumed real Aion character names never contain a space, which
/// is true, but it silently also assumed every NPC name DOES contain one -- false for single-word
/// named/rank bosses (found by the user: "Ulsaruk" showed up as a top damage "player" after a
/// raid). Checking the name against this list catches those without touching the space heuristic,
/// which still does the rest of the job for ordinary multi-word mob names.
/// </summary>
public static class NpcDatabase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlySet<string>? _cache;

    /// <summary>Loads and caches the name set on first use. Returns an empty set (not an exception) if the data file is missing.</summary>
    private static IReadOnlySet<string> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        string path = Path.Combine(AppContext.BaseDirectory, "assets", "npcs", "npcs_en_4x.json");
        var result = new HashSet<string>();

        if (File.Exists(path))
        {
            try
            {
                var rows = JsonSerializer.Deserialize<List<NpcRow>>(File.ReadAllText(path), JsonOptions) ?? new List<NpcRow>();
                foreach (var row in rows)
                {
                    if (row.Name is { Length: > 0 } name)
                    {
                        result.Add(name);
                    }
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[NpcDatabase] Failed to parse {path}: {ex.Message}");
            }
        }

        _cache = result;
        return result;
    }

    /// <summary>True if this exact name matches a cataloged 4.x NPC/monster -- a heuristic, not
    /// proof: a player could in principle pick a name identical to a boss's, and this dataset
    /// doesn't cover every mob ever added to a private server's own content. Still strictly better
    /// than the space-only check alone, which this complements rather than replaces.</summary>
    public static bool IsKnownNpc(string name) => Load().Contains(name);
}

file sealed class NpcRow
{
    public string? Name { get; set; }
}
