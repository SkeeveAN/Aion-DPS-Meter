using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Aion 2 boss NPC ids -> boss name and instance, from assets/aion2/npcs/boss_npcs.json. The game
/// sends the NPC id in the frame that announces a monster (see Aion2FrameDecoder.DecodeNpcSpawn);
/// the combat frames themselves carry only a per-spawn entity id, so this table is what turns "entity
/// 30410" into "Enhanced Harcon". Only bosses are listed - an id that is not here is an ordinary mob.
/// </summary>
public static class Aion2BossCatalog
{
    public sealed record BossInfo(int NpcId, string Name, string Instance);

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "npcs", "boss_npcs.json");
    private static IReadOnlyDictionary<int, BossInfo>? _cache;

    public static BossInfo? Find(int npcId) => Load().GetValueOrDefault(npcId);

    /// <summary>A field or Abyss world boss: its NPC id is 210xxxx (Verteron), 240xxxx (Altgard) or 260xxxx (Abyss); the instance bosses are 23xxxxx.</summary>
    public static bool IsWorldBoss(int npcId) => npcId / 10000 is 210 or 240 or 260;

    private static IReadOnlyDictionary<int, BossInfo> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<int, BossInfo>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty entry in doc.RootElement.GetProperty("bosses").EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out int id))
                    {
                        table[id] = new BossInfo(id, entry.Value.GetProperty("name").GetString() ?? "", entry.Value.GetProperty("instance").GetString() ?? "");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Missing or broken table: no boss is recognised, the meter keeps working.
        }

        _cache = table;
        return table;
    }
}
