using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>One world map of the game as the pet map draws it: its tiles (assets/aion2/maps/&lt;key&gt;), the size of a tile and the grid, and
/// how a world coordinate becomes a pixel of the whole map (<c>pixel = offset + scale * world</c>, no flip, no rotation).</summary>
public sealed record Aion2MapInfo(string Key, string Title, string Table, int Tile, int Grid, double Scale, double OffsetX, double OffsetY);

/// <summary>
/// The three world maps (Verteron, Altgard, the Abyss), the spawn points of the pet monsters on them (pets/spawns.json, read out of the client's
/// MapData.dat files) and what the pet map needs from both: which map the player is on, and where the monsters of the chosen pets stand.
/// Built by Tools/aion2-maps (calibration against recordings and the game's own map screenshots).
/// </summary>
public static class Aion2Maps
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "assets", "aion2");
    private static List<Aion2MapInfo>? _maps;
    private static Dictionary<string, Dictionary<int, float[]>>? _spawns;

    public static IReadOnlyList<Aion2MapInfo> All => _maps ??= LoadMaps();

    public static string TileFolder(Aion2MapInfo map) => Path.Combine(Folder, "maps", map.Key);

    public static (double X, double Y) ToNative(Aion2MapInfo map, double worldX, double worldY) =>
        (map.OffsetX + map.Scale * worldX, map.OffsetY + map.Scale * worldY);

    /// <summary>
    /// The map the player is on, from the pet monsters announced around him: each monster that stands (within 40 m) at a spawn point of its
    /// species on a map votes for that map. Null when none of the monsters seen is a pet monster (then the caller keeps what it knew).
    /// </summary>
    public static Aion2MapInfo? Detect(IReadOnlyList<(int NpcId, float X, float Y)> mobs)
    {
        var spawns = Spawns();
        var votes = new Dictionary<string, int>();
        foreach ((int npc, float x, float y) in mobs)
        {
            foreach (Aion2MapInfo map in All)
            {
                if (spawns.TryGetValue(map.Table, out var table) && table.TryGetValue(npc, out float[]? points) && Near(points, x, y, 4000f))
                {
                    votes[map.Key] = votes.GetValueOrDefault(map.Key) + 1;
                }
            }
        }

        string? best = votes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        return best is null ? null : All.First(m => m.Key == best);
    }

    private static bool Near(float[] xy, float x, float y, float limit)
    {
        float limit2 = limit * limit;
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            float dx = xy[i] - x, dy = xy[i + 1] - y;
            if (dx * dx + dy * dy <= limit2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every spawn point on a map of a monster that drops one of the pets (the pet it belongs to with it).</summary>
    public static IReadOnlyList<(int PetId, float X, float Y)> PointsOf(Aion2MapInfo map, ISet<int> pets)
    {
        var result = new List<(int, float, float)>();
        if (pets.Count == 0 || !Spawns().TryGetValue(map.Table, out var table))
        {
            return result;
        }

        foreach ((int npc, float[] points) in table)
        {
            foreach (int pet in Aion2Pets.PetsOfNpc(npc))
            {
                if (pets.Contains(pet))
                {
                    for (int i = 0; i + 1 < points.Length; i += 2)
                    {
                        result.Add((pet, points[i], points[i + 1]));
                    }
                }
            }
        }

        return result;
    }

    private static List<Aion2MapInfo> LoadMaps()
    {
        var list = new List<Aion2MapInfo>();
        try
        {
            string path = Path.Combine(Folder, "maps", "maps.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var m in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var v = m.Value;
                    list.Add(new Aion2MapInfo(m.Name, v.GetProperty("title").GetString() ?? m.Name, v.GetProperty("table").GetString() ?? "",
                        v.GetProperty("tile").GetInt32(), v.GetProperty("grid").GetInt32(), v.GetProperty("scale").GetDouble(),
                        v.GetProperty("offsetX").GetDouble(), v.GetProperty("offsetY").GetDouble()));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no maps: the pet map stays off
        }

        return list;
    }

    private static Dictionary<string, Dictionary<int, float[]>> Spawns()
    {
        if (_spawns is not null)
        {
            return _spawns;
        }

        var result = new Dictionary<string, Dictionary<int, float[]>>();
        try
        {
            string path = Path.Combine(Folder, "pets", "spawns.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var table = new Dictionary<int, float[]>();
                    foreach (var npc in map.Value.EnumerateObject())
                    {
                        var xy = new List<float>();
                        foreach (var point in npc.Value.EnumerateArray())
                        {
                            xy.Add((float)point[0].GetDouble());
                            xy.Add((float)point[1].GetDouble());
                        }

                        table[int.Parse(npc.Name)] = xy.ToArray();
                    }

                    result[map.Name] = table;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // no spawn points: the pet map stays empty
        }

        return _spawns = result;
    }
}
