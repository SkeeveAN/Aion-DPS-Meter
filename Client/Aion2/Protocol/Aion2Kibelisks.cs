using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>One Kibelisk (teleport artifact) of a world map: its place name in every language and where it stands.</summary>
public sealed record Aion2Kibelisk(IReadOnlyDictionary<string, string> Names, float X, float Y)
{
    public string NameIn(string language) => Names.GetValueOrDefault(language) ?? Names.GetValueOrDefault("en") ?? "";
}

/// <summary>
/// The Kibelisks of Verteron, Altgard and the Abyss (assets/aion2/maps/kibelisks.json, built by Tools/aion2-dat/build_kibelisks.py from the client's own
/// map files and texts). The Rare tab of the pet settings names the nearest one to a spawn point.
/// </summary>
public static class Aion2Kibelisks
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "maps", "kibelisks.json");
    private static Dictionary<string, List<Aion2Kibelisk>>? _byMap;

    /// <summary>The Kibelisks of a map (by map key: verteron, altgard, abyss); empty for an unknown map.</summary>
    public static IReadOnlyList<Aion2Kibelisk> Of(string mapKey) =>
        (_byMap ??= Load()).TryGetValue(mapKey, out var list) ? list : new List<Aion2Kibelisk>();

    private static Dictionary<string, List<Aion2Kibelisk>> Load()
    {
        var result = new Dictionary<string, List<Aion2Kibelisk>>();
        try
        {
            if (File.Exists(FilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    var list = new List<Aion2Kibelisk>();
                    foreach (var k in map.Value.EnumerateArray())
                    {
                        var names = k.GetProperty("names").EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "");
                        list.Add(new Aion2Kibelisk(names, (float)k.GetProperty("x").GetDouble(), (float)k.GetProperty("y").GetDouble()));
                    }

                    result[map.Name] = list;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no Kibelisks: the cut-outs just do not name one
        }

        return result;
    }
}
