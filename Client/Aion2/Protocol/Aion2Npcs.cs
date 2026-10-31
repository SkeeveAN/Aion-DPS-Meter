using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Every monster's name by the NPC id the game sends when it appears, read from the client's NpcData table and
/// its texts (assets/aion2/npcs/npc_names.json: {"2300709": {"en": "Talisra of the Void", "de": "..."}}, English,
/// German, French, Spanish and Russian). The bosses are in <see cref="Aion2BossCatalog"/> too; this covers the rest.
/// </summary>
public static class Aion2Npcs
{
    private static readonly string FilePath = AssetStore.Path("aion2", "npcs", "npc_names.json");
    private static readonly string SummonFilePath = AssetStore.Path("aion2", "npcs", "player_summon_npcs.json");
    private static Dictionary<int, Dictionary<string, string>>? _table;
    private static HashSet<int>? _playerSummons;

    /// <summary>True for a monster the NPC table knows that is not one of the player summons (pet, trap, aura, spirit):
    /// a spawn of it that names a player names its target (a Blazing Totem naming the player it burns), not an owner.
    /// False for the player summons, and for ids neither list knows.</summary>
    public static bool IsMonsterNotPlayerSummon(int npcId)
    {
        var table = _table ??= Load();
        var summons = _playerSummons ??= LoadSummons();
        return summons.Count > 0 && table.ContainsKey(npcId) && !summons.Contains(npcId);
    }

    /// <summary>True for a player summon (pet, trap, aura, spirit) of the game's NpcData; false for everything else and when the list is missing.</summary>
    public static bool IsPlayerSummon(int npcId) => (_playerSummons ??= LoadSummons()).Contains(npcId);

    private static HashSet<int> LoadSummons()
    {
        try
        {
            if (File.Exists(SummonFilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(SummonFilePath));
                return doc.RootElement.GetProperty("npcs").EnumerateArray().Select(e => e.GetInt32()).ToHashSet();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // No list: every named spawn stays an owner claim, as before.
        }

        return new HashSet<int>();
    }

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
