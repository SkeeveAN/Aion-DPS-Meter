using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Every monster's name by the NPC id the game sends when it appears, read from the client's NpcData table and
/// its texts (assets/aion2/npcs/npc_names.json: {"2300709": {"en": "Talisra of the Void", "de": "..."}}, English,
/// German, French, Spanish and Russian). The bosses are in <see cref="Aion2BossCatalog"/> too; this covers the rest.
/// </summary>
public static class Aion2Npcs
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "npcs", "npc_names.json");
    private static Dictionary<int, Dictionary<string, string>>? _table;

    /// <summary>The monster's name in a language, else English; null when the NPC id is unknown.</summary>
    public static string? NameOf(int npcId, string language)
    {
        var table = _table ??= Load();
        return table.TryGetValue(npcId, out var names) ? names.GetValueOrDefault(language) ?? names.GetValueOrDefault("en") : null;
    }

    private static Dictionary<int, Dictionary<string, string>> Load()
    {
        var table = new Dictionary<int, Dictionary<string, string>>();
        try
        {
            if (File.Exists(FilePath))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(FilePath)) ?? new();
                foreach ((string key, var names) in raw)
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
            // No table: monsters stay unnamed.
        }

        return table;
    }
}
