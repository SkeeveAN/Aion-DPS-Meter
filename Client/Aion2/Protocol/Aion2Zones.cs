using System.IO;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Protocol;

/// <summary>What a zone id of the game's zone frame (0x2136) is: an open map or an instance, and its name as the game client shows it
/// (assets/aion2/zones/zones.json). Ids the file does not list are told apart by their range alone (6xxxxx are instances) and have no name.</summary>
public static class Aion2Zones
{
    private static readonly string FilePath = AssetStore.Path("aion2", "zones", "zones.json");
    private static Dictionary<int, (bool Instance, Dictionary<string, string> Names)>? _zones;

    /// <summary>Whether the zone is an instance (a dungeon you enter and leave) and not an open map.</summary>
    public static bool IsInstance(int zoneId) =>
        Table().TryGetValue(zoneId, out var zone) ? zone.Instance : zoneId is >= 600000 and < 700000;

    /// <summary>The 10 against 10 arena (zone 62, "Odyle Shard Cradle", seen on 2026-10-22). The game evens everybody out there: the combat power is the
    /// same fixed number for all (397228) and every skill and stigma is at level 20, so what a client reads of a player there is not his real state.
    /// Only this zone is verified.</summary>
    public static bool IsEqualizedArena(int zoneId) => zoneId == 62;

    /// <summary>The arenas (60 Fire Temple Arena, 61 Impetusium Arena, 62 the 10 against 10 arena): the players met there are uploaded once the fight is over.</summary>
    public static bool IsArena(int zoneId) => zoneId is 60 or 61 or 62;

    /// <summary>The zone's name in a language, else English; null when the id is not in the file.</summary>
    public static string? NameOf(int zoneId, string language) =>
        Table().TryGetValue(zoneId, out var zone) ? zone.Names.GetValueOrDefault(language) ?? zone.Names.GetValueOrDefault("en") : null;

    private static Dictionary<int, (bool Instance, Dictionary<string, string> Names)> Table() => _zones ??= Load();

    private static Dictionary<int, (bool, Dictionary<string, string>)> Load()
    {
        var table = new Dictionary<int, (bool, Dictionary<string, string>)>();
        try
        {
            if (File.Exists(FilePath))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonElement zone in doc.RootElement.GetProperty("zones").EnumerateArray())
                {
                    var names = new Dictionary<string, string>();
                    foreach (JsonProperty name in zone.GetProperty("names").EnumerateObject())
                    {
                        if (name.Value.GetString() is { Length: > 0 } text)
                        {
                            names[name.Name] = text;
                        }
                    }

                    table[zone.GetProperty("id").GetInt32()] = (zone.GetProperty("kind").GetString() == "instance", names);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // No zone names then: the session list shows numbers instead.
        }

        return table;
    }
}
