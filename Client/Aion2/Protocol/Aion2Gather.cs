using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// The collectibles of the world maps (maps/gather.json, built by Tools/aion2-dat/build_gather.py out of the client's MapData.dat and EnvObjData):
/// the placements of gather sources (Od, herbs, food plants, ores, wood, gems) and of the region "traces", by kind. The interactive map draws them.
/// </summary>
public static class Aion2Gather
{
    /// <summary>The kinds in the order the settings list them.</summary>
    public static readonly string[] Kinds = { "Od", "Herb", "Food", "Ore", "Wood", "Gemstone", "Fragment" };

    private static Dictionary<string, Dictionary<string, List<float[]>>>? _maps;   // map table -> kind -> x,y pairs

    /// <summary>The placements of the chosen kinds on a map.</summary>
    public static IReadOnlyList<(string Kind, float X, float Y)> PointsOf(Aion2MapInfo map, ISet<string> kinds)
    {
        var result = new List<(string, float, float)>();
        if (kinds.Count == 0 || !Maps().TryGetValue(map.Table, out var perKind))
        {
            return result;
        }

        foreach ((string kind, List<float[]> points) in perKind)
        {
            if (kinds.Contains(kind))
            {
                result.AddRange(points.Select(p => (kind, p[0], p[1])));
            }
        }

        return result;
    }

    /// <summary>The kinds that have at least one placement on some map (what the settings offer).</summary>
    public static IReadOnlyList<string> AvailableKinds() =>
        Kinds.Where(k => Maps().Values.Any(m => m.ContainsKey(k))).ToList();

    private static Dictionary<string, Dictionary<string, List<float[]>>> Maps()
    {
        if (_maps is not null)
        {
            return _maps;
        }

        var result = new Dictionary<string, Dictionary<string, List<float[]>>>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "gather.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var kindOf = doc.RootElement.GetProperty("objects").EnumerateObject()
                    .ToDictionary(o => o.Name, o => o.Value.GetProperty("kind").GetString() ?? "");
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var perKind = new Dictionary<string, List<float[]>>();
                    foreach (var obj in map.Value.EnumerateObject())
                    {
                        if (!kindOf.TryGetValue(obj.Name, out string? kind) || kind.Length == 0)
                        {
                            continue;
                        }

                        if (!perKind.TryGetValue(kind, out var list))
                        {
                            perKind[kind] = list = new List<float[]>();
                        }

                        list.AddRange(obj.Value.EnumerateArray().Select(p => new[] { (float)p[0].GetDouble(), (float)p[1].GetDouble() }));
                    }

                    result[map.Name] = perKind;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no collectibles: the map shows only the pets
        }

        return _maps = result;
    }
}
