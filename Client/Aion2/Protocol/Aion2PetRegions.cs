using System.IO;
using System.Text.Json;

namespace AionDPS.Aion2.Protocol;

/// <summary>One named region of a world map and the pets that have a spawn point in it.</summary>
public sealed record PetRegion(string Key, IReadOnlyDictionary<string, string> Names, IReadOnlyList<int> Pets)
{
    /// <summary>The region's name in a language, else English.</summary>
    public string NameIn(string language) => Names.TryGetValue(language, out string? n) ? n : Names.GetValueOrDefault("en") ?? Key;
}

/// <summary>Verteron, Altgard or the Abyss, with its regions.</summary>
public sealed record PetRegionGroup(string Id, IReadOnlyList<PetRegion> Regions);

/// <summary>
/// Which pets live in which region (assets/aion2/pets/regions.json, built by Tools/aion2-dat/build_pet_regions.py from the client's own
/// area volumes and world map tables). Only for the information tab of the pet settings; nothing in it is switched on or off.
/// </summary>
public static class Aion2PetRegions
{
    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "regions.json");
    private static IReadOnlyList<PetRegionGroup>? _groups;

    public static IReadOnlyList<PetRegionGroup> Groups => _groups ??= Load();

    private static IReadOnlyList<PetRegionGroup> Load()
    {
        var groups = new List<PetRegionGroup>();
        try
        {
            if (!File.Exists(FilePath))
            {
                return groups;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            foreach (var g in doc.RootElement.GetProperty("groups").EnumerateArray())
            {
                var regions = new List<PetRegion>();
                foreach (var r in g.GetProperty("regions").EnumerateArray())
                {
                    var names = r.GetProperty("names").EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "");
                    regions.Add(new PetRegion(r.GetProperty("key").GetString() ?? "", names, r.GetProperty("pets").EnumerateArray().Select(v => v.GetInt32()).ToList()));
                }

                groups.Add(new PetRegionGroup(g.GetProperty("id").GetString() ?? "", regions));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // an unreadable file leaves the tab empty
        }

        return groups;
    }
}
