using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// How long a boss takes to rage, from assets/aion2/npcs/boss_rage.json (the client table NpcData.BossBerserkInvokeTime): most bosses 300 s,
/// a few more or less (Vakron 210 s, Bargott and Kaldrix 480 s, Eternal Ludra 600 s). A boss that is not listed counts as 300 s.
/// </summary>
public static class Aion2BossRage
{
    public const int DefaultSeconds = 300;

    private static readonly string FilePath = AssetStore.Path("aion2", "npcs", "boss_rage.json");
    private static IReadOnlyDictionary<int, int>? _cache;

    public static int SecondsFor(int? npcId) => npcId is int id && Load().TryGetValue(id, out int seconds) ? seconds : DefaultSeconds;

    private static IReadOnlyDictionary<int, int> Load()
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        var table = new Dictionary<int, int>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty entry in doc.RootElement.GetProperty("rage").EnumerateObject())
                {
                    if (int.TryParse(entry.Name, out int id) && entry.Value.TryGetInt32(out int seconds) && seconds > 0)
                    {
                        table[id] = seconds;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Missing or broken table: every boss counts as 300 s, the meter keeps working.
        }

        _cache = table;
        return table;
    }
}
