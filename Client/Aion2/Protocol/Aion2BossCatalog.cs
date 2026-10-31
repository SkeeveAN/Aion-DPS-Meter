using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Aion 2 boss NPC ids -> boss name and instance, from assets/aion2/npcs/boss_npcs.json. The game
/// sends the NPC id in the frame that announces a monster (see Aion2FrameDecoder.DecodeNpcSpawn);
/// the combat frames themselves carry only a per-spawn entity id, so this table is what turns "entity
/// 30410" into "Enhanced Harcon". Only bosses are listed - an id that is not here is an ordinary mob.
/// An entry may carry <c>maxHp</c>: the server sends no maximum, so a meter that joins mid-fight would take the first reading for it.
/// <c>hpScale</c> "party" marks an exploration boss whose hit points grow with the group: <c>maxHp</c> is then the value for one player.
/// </summary>
public static class Aion2BossCatalog
{
    public sealed record BossInfo(int NpcId, string Name, string Instance, long MaxHp = 0, bool PartyScaled = false);

    private static readonly string FilePath = AssetStore.Path("aion2", "npcs", "boss_npcs.json");
    private static IReadOnlyDictionary<int, BossInfo>? _cache;

    public static BossInfo? Find(int npcId) => Load().GetValueOrDefault(npcId);

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
                        table[id] = new BossInfo(id, entry.Value.GetProperty("name").GetString() ?? "", entry.Value.GetProperty("instance").GetString() ?? "",
                            entry.Value.TryGetProperty("maxHp", out JsonElement hp) && hp.TryGetInt64(out long maxHp) ? maxHp : 0,
                            entry.Value.TryGetProperty("hpScale", out JsonElement scale) && scale.ValueKind == JsonValueKind.String && scale.GetString() == "party");
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
