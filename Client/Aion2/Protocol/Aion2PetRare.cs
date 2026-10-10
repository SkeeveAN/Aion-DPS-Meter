using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AionDPS.Aion2.Protocol;

/// <summary>One place an instance mob stands (an instance by its name in every language) with the number of its spawn points there.</summary>
public sealed record PetRarePlace(IReadOnlyDictionary<string, string> Names, int Count)
{
    public string NameIn(string language) => Names.GetValueOrDefault(language) ?? Names.GetValueOrDefault("en") ?? "";
}

/// <summary>
/// One line of the Rare tab (the instance and world boss lines are grouped further by <see cref="SubKey"/>: the instance, or the map of the boss; for the maps
/// verteron, altgard and abyss the sub group has no names, the caller uses its names for the maps): a mob of a pet with few spawn points on a map (<see cref="Category"/> = the map key), a mob of an instance (<c>instances</c>) or
/// a world boss that drops a pet soul (<c>worldbosses</c>, all its spawn points) or a pet that has no spawn point at all (<c>unknown</c>, <see cref="NpcId"/> 0).
/// </summary>
public sealed record PetRareEntry(string Key, string Category, int PetId, int NpcId, int Count, string? MapKey,
    IReadOnlyList<(float X, float Y)> Spots, IReadOnlyList<(float X, float Y)> OtherSpots,
    int PetOnMap, IReadOnlyDictionary<string, int> PetOnOtherMaps, int PetInstancePoints, IReadOnlyList<PetRarePlace> Places,
    string? SubKey = null, IReadOnlyDictionary<string, string>? SubNames = null);

/// <summary>
/// Which pet mobs are rare: the spawn points of pets.json's monsters (spawns.json) counted per pet and per map. The three maps are not added up (the
/// factions do not share Verteron and Altgard): a pet is listed under a map when all its mobs there together have at most <c>rareMax</c> spawn points.
/// A pet that is plentiful in instances (<c>instanceMin</c> points or more, each instance counted once by its largest difficulty) is not listed on the map,
/// only under Instances; the Instances list holds the instance mobs of every pet that is rare on all maps it is on. Pets without any spawn point are
/// Unknown. The field bosses (Aion2FieldBosses) that drop a pet are listed under World bosses with their spawn points, whatever the numbers, and do not count
/// for the maps. Event and quest phases of the world maps (InstanceLayer), maps the client names nothing and the maps the EU game lacks do not count.
/// </summary>
public static class Aion2PetRare
{
    public const string Instances = "instances";
    public const string WorldBosses = "worldbosses";
    public const string Unknown = "unknown";

    private sealed class Mob
    {
        public Dictionary<string, List<(float X, float Y)>> World { get; } = new();
        public Dictionary<string, int> Places { get; } = new();
        public bool IsBoss { get; set; }
        /// <summary>A world boss: its spawn points by map file.</summary>
        public Dictionary<string, List<(float X, float Y)>> BossSpots { get; } = new();
    }

    private sealed class Catalog
    {
        public Dictionary<int, Dictionary<int, Mob>> Pets { get; } = new();
        public Dictionary<string, Dictionary<string, string>> PlaceNames { get; } = new();
    }

    /// <summary>Maps in the client's files that the EU game does not have yet (Chaotic Middle and Upper Reshanta): their spawn points are left out.</summary>
    private static readonly string[] NotInEu = { "Intersever/Abyss/Abyss_Reshanta_C", "Intersever/Abyss/Abyss_Reshanta_D" };

    private static readonly Regex Difficulty = new("_(Easy|Normal|Hard|Hell|Extreme|Despair)$", RegexOptions.Compiled);
    private static Catalog? _catalog;

    private static Catalog Data => _catalog ??= Build();

    public static IReadOnlyDictionary<string, List<PetRareEntry>> Compute(int rareMax, int instanceMin)
    {
        var data = Data;
        var result = new Dictionary<string, List<PetRareEntry>> { [Instances] = new(), [WorldBosses] = new(), [Unknown] = new() };
        foreach (var map in Aion2Maps.All)
        {
            result[map.Key] = new();
        }

        foreach (int petId in Aion2Pets.AllPetIds)
        {
            var mobs = data.Pets.GetValueOrDefault(petId) ?? new Dictionary<int, Mob>();
            AddBosses(result, data, petId, mobs);
            mobs = mobs.Where(kv => !kv.Value.IsBoss).ToDictionary(kv => kv.Key, kv => kv.Value);
            var onMap = Aion2Maps.All.ToDictionary(m => m.Key, m => mobs.Values.Sum(mob => mob.World.GetValueOrDefault(m.Key)?.Count ?? 0));
            int instancePoints = mobs.Values.Sum(mob => mob.Places.Values.Sum());
            if (onMap.Values.Sum() == 0 && instancePoints == 0 && !data.Pets.GetValueOrDefault(petId, new()).Values.Any(m => m.IsBoss))
            {
                result[Unknown].Add(new PetRareEntry($"u{petId}", Unknown, petId, 0, 0, null, Array.Empty<(float, float)>(), Array.Empty<(float, float)>(), 0,
                    new Dictionary<string, int>(), 0, Array.Empty<PetRarePlace>()));
                continue;
            }

            // the maps are not added up: a pet is rare on a map when it has few spawn points on that very map
            if (instancePoints > 0 && onMap.Values.Max() <= rareMax)
            {
                foreach ((int npc, Mob mob) in mobs)
                {
                    var places = mob.Places.Select(p => new PetRarePlace(data.PlaceNames[p.Key], p.Value)).OrderByDescending(p => p.Count).ToList();
                    foreach ((string place, int count) in mob.Places)
                    {
                        result[Instances].Add(new PetRareEntry($"i{npc}.{petId}.{place}", Instances, petId, npc, count, null, Array.Empty<(float, float)>(), Array.Empty<(float, float)>(),
                            0, onMap.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value), instancePoints, places, place, data.PlaceNames[place]));
                    }
                }
            }

            if (instancePoints >= instanceMin)
            {
                continue;
            }

            foreach (var map in Aion2Maps.All)
            {
                if (onMap[map.Key] == 0 || onMap[map.Key] > rareMax)
                {
                    continue;
                }

                foreach ((int npc, Mob mob) in mobs)
                {
                    if (!mob.World.TryGetValue(map.Key, out var spots))
                    {
                        continue;
                    }

                    var others = mobs.Where(kv => kv.Key != npc).SelectMany(kv => kv.Value.World.GetValueOrDefault(map.Key) ?? new()).ToList();
                    result[map.Key].Add(new PetRareEntry($"{map.Key}.{npc}.{petId}", map.Key, petId, npc, spots.Count, map.Key, spots, others, onMap[map.Key],
                        onMap.Where(kv => kv.Value > 0 && kv.Key != map.Key).ToDictionary(kv => kv.Key, kv => kv.Value), instancePoints, Array.Empty<PetRarePlace>()));
                }
            }
        }

        return result;
    }

    /// <summary>The world bosses among a pet's mobs: one line each, on the map when the boss stands on one of the three maps with a picture.</summary>
    private static void AddBosses(Dictionary<string, List<PetRareEntry>> result, Catalog data, int petId, Dictionary<int, Mob> mobs)
    {
        var worldByTable = Aion2Maps.All.ToDictionary(m => m.Table, m => m.Key);
        foreach ((int npc, Mob mob) in mobs.Where(kv => kv.Value.IsBoss))
        {
            int total = mob.BossSpots.Values.Sum(l => l.Count);
            foreach ((string table, var spots) in mob.BossSpots)
            {
                string? mapKey = worldByTable.GetValueOrDefault(table);
                var places = new List<PetRarePlace>();
                var field = Aion2FieldBosses.Maps.FirstOrDefault(m => table.EndsWith("/" + m.Key, StringComparison.Ordinal));
                var names = field?.Names ?? new Dictionary<string, string> { ["en"] = table.Split('/')[^1] };
                if (mapKey is null)
                {
                    places.Add(new PetRarePlace(names, spots.Count));
                }

                // Verteron, Altgard and the Abyss are the caller's own names; the second maps (Eltnen, Morheim) keep the game's
                string sub = table.Contains("World_L_A", StringComparison.Ordinal) ? "verteron" : table.Contains("World_D_A", StringComparison.Ordinal) ? "altgard"
                    : table.Contains("/Abyss/", StringComparison.Ordinal) ? "abyss" : table.Split('/')[^1];
                result[WorldBosses].Add(new PetRareEntry($"b{npc}.{petId}.{table}", WorldBosses, petId, npc, spots.Count, mapKey, spots, Array.Empty<(float, float)>(), total,
                    new Dictionary<string, int>(), 0, places, sub, sub is "verteron" or "altgard" or "abyss" ? null : names));
            }
        }
    }

    private static Catalog Build()
    {
        var catalog = new Catalog();
        var names = LoadInstanceNames();
        var worldByTable = Aion2Maps.All.ToDictionary(m => m.Table, m => m.Key);
        var bosses = Aion2FieldBosses.Maps.SelectMany(m => m.Bosses.Select(b => b.Npc)).ToHashSet();
        foreach ((string table, var npcs) in Aion2Maps.SpawnTables)
        {
            if (table.Contains("/InstanceLayer/", StringComparison.Ordinal) || NotInEu.Contains(table))
            {
                continue;
            }

            worldByTable.TryGetValue(table, out string? mapKey);
            string? group = null;
            if (mapKey is null && names.TryGetValue(table, out var known) && known.Count > 0)
            {
                group = PlaceKey(table, names, catalog); // maps the client gives no name (test maps, event and quest phases) are left out
            }

            foreach ((int npc, float[] points) in npcs)
            {
                if (mapKey is null && group is null && !bosses.Contains(npc))
                {
                    continue;
                }

                foreach (int petId in Aion2Pets.PetsOfNpc(npc))
                {
                    if (!catalog.Pets.TryGetValue(petId, out var mobs))
                    {
                        catalog.Pets[petId] = mobs = new Dictionary<int, Mob>();
                    }

                    if (!mobs.TryGetValue(npc, out var mob))
                    {
                        mobs[npc] = mob = new Mob();
                    }

                    if (bosses.Contains(npc))
                    {
                        mob.IsBoss = true;
                        mob.BossSpots[table] = Enumerable.Range(0, points.Length / 2).Select(i => (points[2 * i], points[2 * i + 1])).ToList();
                    }
                    else if (mapKey is not null)
                    {
                        var list = new List<(float, float)>(points.Length / 2);
                        for (int i = 0; i + 1 < points.Length; i += 2)
                        {
                            list.Add((points[i], points[i + 1]));
                        }

                        mob.World[mapKey] = list;
                    }
                    else if (group is not null)
                    {
                        // the difficulties of one dungeon share a name: counted once, by the largest
                        mob.Places[group] = Math.Max(mob.Places.GetValueOrDefault(group), points.Length / 2);
                    }
                }
            }
        }

        return catalog;
    }

    /// <summary>The name key of an instance map (its English name, else the folder name made readable); the names in every language go to the catalog.</summary>
    private static string PlaceKey(string table, Dictionary<string, Dictionary<string, string>> names, Catalog catalog)
    {
        names.TryGetValue(table, out var known);
        string key = known is not null && known.TryGetValue("en", out string? en) ? en : Readable(table);
        if (!catalog.PlaceNames.ContainsKey(key))
        {
            catalog.PlaceNames[key] = known is { Count: > 0 } ? known : new Dictionary<string, string> { ["en"] = key };
        }

        return key;
    }

    private static string Readable(string table)
    {
        string last = Difficulty.Replace(table.Split('/')[^1], "");
        return Regex.Replace(last, "(?<=[a-z])(?=[A-Z])|_", " ").Trim();
    }

    private static Dictionary<string, Dictionary<string, string>> LoadInstanceNames()
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "pets", "instances.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    result[map.Name] = map.Value.EnumerateObject().ToDictionary(n => n.Name, n => n.Value.GetString() ?? "");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no names: the instances show up under their file names
        }

        return result;
    }
}
