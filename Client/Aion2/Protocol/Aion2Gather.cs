using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>One collectible the player can pick: its kind, its name (the key is the English one), and how many fixed places the world maps hold for it.</summary>
public sealed record Aion2GatherItem(string Kind, string Key, IReadOnlyDictionary<string, string> Names, int Count)
{
    public string NameIn(string language) => Names.GetValueOrDefault(language) ?? Key;
}

/// <summary>
/// The collectibles of the world maps (maps/gather.json, built by Tools/aion2-dat/build_gather.py out of the client's MapData.dat and EnvObjData):
/// every gather source of the game (Od, herbs, food plants, ores, wood, cotton, gems) and the region "traces", grouped by kind, with the places
/// the world maps hold for them. The settings list the items, the interactive map draws the chosen ones.
/// </summary>
public static class Aion2Gather
{
    /// <summary>The kinds in the order the settings list them.</summary>
    public static readonly string[] Kinds = { "Od", "Herb", "Food", "Ore", "Wood", "Cotton", "Gemstone", "Fragment" };

    private static List<Aion2GatherItem>? _items;
    private static Dictionary<string, Dictionary<string, List<float[]>>>? _maps;   // map table -> item key -> x,y pairs

    /// <summary>Every collectible, one entry per kind and name (the variants of a level count together).</summary>
    public static IReadOnlyList<Aion2GatherItem> Items()
    {
        Load();
        return _items!;
    }

    /// <summary>The places of the chosen items (by key) on a map, each with its kind.</summary>
    public static IReadOnlyList<(string Kind, float X, float Y)> PointsOf(Aion2MapInfo map, ISet<string> keys) =>
        NamedPointsOf(map, keys).Select(p => (p.Item.Kind, p.X, p.Y)).ToList();

    /// <summary>The places of the chosen items (by key) on a map, each with its item (for the list next to the map).</summary>
    public static IReadOnlyList<(Aion2GatherItem Item, float X, float Y)> NamedPointsOf(Aion2MapInfo map, ISet<string> keys)
    {
        Load();
        var result = new List<(Aion2GatherItem, float, float)>();
        if (keys.Count == 0 || !_maps!.TryGetValue(map.Table, out var perItem))
        {
            return result;
        }

        foreach (var item in _items!.Where(i => keys.Contains(i.Key)))
        {
            if (perItem.TryGetValue(item.Key, out var points))
            {
                result.AddRange(points.Select(p => (item, p[0], p[1])));
            }
        }

        return result;
    }

    private static void Load()
    {
        if (_items is not null)
        {
            return;
        }

        var items = new Dictionary<(string Kind, string Key), (Dictionary<string, string> Names, int Count)>();
        var maps = new Dictionary<string, Dictionary<string, List<float[]>>>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "gather.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var keyOf = new Dictionary<string, (string Kind, string Key)>();
                Dictionary<string, string>? odNames = null;
                foreach (var o in doc.RootElement.GetProperty("objects").EnumerateObject())
                {
                    string kind = o.Value.GetProperty("kind").GetString() ?? "";
                    var names = o.Value.GetProperty("names").EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "");
                    // an Od deposit is one source: its four levels and the special variants (splendent, mysterious) all count as plain Od
                    if (kind == "Od")
                    {
                        odNames ??= names;
                        names = odNames;
                    }

                    string key = names.GetValueOrDefault("en") ?? o.Name;
                    keyOf[o.Name] = (kind, key);
                    int count = o.Value.GetProperty("count").GetInt32();
                    items[(kind, key)] = items.TryGetValue((kind, key), out var have) ? (have.Names, have.Count + count) : (names, count);
                }

                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var perItem = new Dictionary<string, List<float[]>>();
                    foreach (var obj in map.Value.EnumerateObject())
                    {
                        if (!keyOf.TryGetValue(obj.Name, out var id))
                        {
                            continue;
                        }

                        if (!perItem.TryGetValue(id.Key, out var list))
                        {
                            perItem[id.Key] = list = new List<float[]>();
                        }

                        list.AddRange(obj.Value.EnumerateArray().Select(p => new[] { (float)p[0].GetDouble(), (float)p[1].GetDouble() }));
                    }

                    maps[map.Name] = perItem;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no collectibles: the map shows only the pets
        }

        _maps = maps;
        _items = items.Select(kv => new Aion2GatherItem(kv.Key.Kind, kv.Key.Key, kv.Value.Names, kv.Value.Count))
            .OrderBy(i => Array.IndexOf(Kinds, i.Kind)).ThenByDescending(i => i.Count > 0).ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
