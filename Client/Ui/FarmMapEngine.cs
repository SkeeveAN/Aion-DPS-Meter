using System.IO;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.Update;

namespace AionDPS.Ui;

/// <summary>One line of a farm spot: a monster (with the pet it drops) standing there, how many of them, and where the pet stands for the player.</summary>
public sealed record FarmLine(string Mob, string Pet, int Count, int Level, int Progress, bool Missing);

/// <summary>A place on a map to farm: the middle, the radius it covers (world units) and what stands there. <see cref="Unsafe"/> = a danger (guard, elite) is near and no better place was there.</summary>
public sealed record FarmSpot(int Number, double X, double Y, double Radius, IReadOnlyList<FarmLine> Lines, bool Unsafe);

/// <summary>Everything the picture of one map needs: the spots, the places of all missing pets, and the dangers.</summary>
public sealed record FarmPlan(Aion2MapInfo Map, IReadOnlyList<FarmSpot> Spots, IReadOnlyList<(float X, float Y)> Points,
    IReadOnlyList<(float X, float Y)> Guards, IReadOnlyList<(float X, float Y)> Elites, int MissingPets);

/// <summary>
/// The farm map: from what the player still lacks (pets under the top level, or not owned) it works out where on a map he farms most effectively next.
/// A spot is a place where several of the missing pets stand close together (30 m), kept away from the dangers (guards of the other side, elite and hero monsters,
/// 35 m); a missing pet that is in no such place gets the densest place of its own. The dangers come from pets/farm_danger.json (Tools/aion2-dat/build_farm_danger.py).
/// </summary>
public static class FarmMapEngine
{
    private const double SpotRadius = 3000, SingleRadius = 7000, DangerDistance = 3500, Separation = 9000;
    private const int MaxSpots = 16, MinPets = 2, MinSpawns = 4;

    private static Dictionary<string, (List<(float, float)> Guards, List<(float, float)> Elites)>? _danger;

    public static void Reload() => _danger = null;

    /// <summary>Works out the spots of one map. <paramref name="isLand"/> says whether a place lies on the drawn land (spawn points in the holes of the game's map are left out); null = all.</summary>
    public static FarmPlan Plan(Aion2MapInfo map, IReadOnlyDictionary<int, Aion2PetState> states, string? faction, string language, Func<float, float, bool>? isLand)
    {
        // the missing pets' spawn points, with the monster each belongs to
        var points = new List<(int Pet, int Npc, float X, float Y)>();
        var seen = new HashSet<(int, float, float)>();
        foreach (string tableName in map.Tables is { Count: > 0 } ? map.Tables : new[] { map.Table })
        {
            if (!Aion2Maps.SpawnTables.TryGetValue(tableName, out var table))
            {
                continue;
            }

            foreach ((int npc, float[] xy) in table)
            {
                foreach (int pet in Aion2Pets.PetsOfNpc(npc))
                {
                    if (states.TryGetValue(pet, out var state) && state.Level >= Aion2Pets.TopLevel)
                    {
                        continue;
                    }

                    for (int i = 0; i + 1 < xy.Length; i += 2)
                    {
                        if ((isLand is null || isLand(xy[i], xy[i + 1])) && seen.Add((pet, xy[i], xy[i + 1])))
                        {
                            points.Add((pet, npc, xy[i], xy[i + 1]));
                        }
                    }

                    break; // a monster counts for the first pet it drops
                }
            }
        }

        var (guards, elites) = DangerOf(map);
        if (!Aion2Guards.IsHostileMap(map.Table, faction))
        {
            guards = new List<(float, float)>(); // the guards of the player's own side are friends
        }

        var dangers = guards.Concat(elites).ToList();
        int n = points.Count;
        var near = new List<int>[n]; // the points within a spot's radius of each point
        var safe = new bool[n];
        for (int a = 0; a < n; a++)
        {
            safe[a] = dangers.All(d => Dist(d.Item1, d.Item2, points[a].X, points[a].Y) > DangerDistance);
            near[a] = new List<int>();
            for (int b = 0; b < n; b++)
            {
                if (Dist(points[a].X, points[a].Y, points[b].X, points[b].Y) <= SpotRadius)
                {
                    near[a].Add(b);
                }
            }
        }

        // 1. places where several missing pets stand together, the best first, each at a distance from the ones before
        var chosen = new List<(int Center, double Radius, bool Unsafe)>();
        var ranked = Enumerable.Range(0, n).Where(a => safe[a])
            .Select(a => (A: a, Pets: near[a].Select(b => points[b].Pet).Distinct().Count(), Count: near[a].Count))
            .Where(c => c.Pets >= MinPets && c.Count >= MinSpawns)
            .OrderByDescending(c => c.Pets).ThenByDescending(c => c.Count).ToList();
        foreach (var c in ranked)
        {
            if (chosen.Count < MaxSpots && chosen.All(o => Dist(points[o.Center].X, points[o.Center].Y, points[c.A].X, points[c.A].Y) > Separation))
            {
                chosen.Add((c.A, SpotRadius, false));
            }
        }

        // 2. a missing pet that is in no chosen place gets its own densest place (a wider one: it stands alone)
        foreach (int pet in points.Select(p => p.Pet).Distinct().ToList())
        {
            if (chosen.Count >= MaxSpots)
            {
                break;
            }

            bool covered = points.Where(p => p.Pet == pet).Any(p => chosen.Any(o => Dist(points[o.Center].X, points[o.Center].Y, p.X, p.Y) <= o.Radius));
            if (covered)
            {
                continue;
            }

            int best = -1, bestCount = -1;
            bool bestSafe = false;
            for (int a = 0; a < n; a++)
            {
                if (points[a].Pet != pet)
                {
                    continue;
                }

                int count = 0;
                for (int b = 0; b < n; b++)
                {
                    if (points[b].Pet == pet && Dist(points[a].X, points[a].Y, points[b].X, points[b].Y) <= SingleRadius)
                    {
                        count++;
                    }
                }

                if ((safe[a] && !bestSafe) || (safe[a] == bestSafe && count > bestCount))
                {
                    best = a;
                    bestCount = count;
                    bestSafe = safe[a];
                }
            }

            if (best >= 0)
            {
                chosen.Add((best, SingleRadius, !bestSafe));
            }
        }

        // the spots: the ones with several pets first, then by how many spawns; each lists what stands in it
        var spots = new List<FarmSpot>();
        var raw = new List<(double X, double Y, double R, bool Unsafe, List<FarmLine> Lines, int Pets, int Count)>();
        foreach (var (center, radius, isUnsafe) in chosen)
        {
            var inside = Enumerable.Range(0, n).Where(b => Dist(points[center].X, points[center].Y, points[b].X, points[b].Y) <= radius).ToList();
            if (radius == SingleRadius)
            {
                inside = inside.Where(b => points[b].Pet == points[center].Pet).ToList();
            }

            double cx = inside.Average(b => (double)points[b].X), cy = inside.Average(b => (double)points[b].Y);
            var lines = inside.GroupBy(b => (points[b].Npc, points[b].Pet))
                .Select(g =>
                {
                    int pet = g.Key.Pet;
                    states.TryGetValue(pet, out var state);
                    string pn = Aion2Pets.PetName(pet, language) ?? $"#{pet}";
                    string mob = Aion2Npcs.NameOf(g.Key.Npc, language) ?? pn;
                    return new FarmLine(mob, pn, g.Count(), state?.Level ?? 0, state?.Progress ?? 0, state is null);
                })
                .GroupBy(l => (l.Mob, l.Pet)).Select(g => g.First() with { Count = g.Sum(l => l.Count) })
                .OrderByDescending(l => l.Count).ToList();
            raw.Add((cx, cy, radius, isUnsafe, lines, lines.Select(l => l.Pet).Distinct().Count(), lines.Sum(l => l.Count)));
        }

        int number = 1;
        foreach (var r in raw.OrderBy(r => r.Unsafe).ThenByDescending(r => r.Pets).ThenByDescending(r => r.Count))
        {
            spots.Add(new FarmSpot(number++, r.X, r.Y, r.R, r.Lines, r.Unsafe));
        }

        return new FarmPlan(map, spots, points.Select(p => (p.X, p.Y)).ToList(), guards, elites, points.Select(p => p.Pet).Distinct().Count());
    }

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    private static (List<(float, float)> Guards, List<(float, float)> Elites) DangerOf(Aion2MapInfo map)
    {
        _danger ??= LoadDanger();
        var guards = new List<(float, float)>();
        var elites = new List<(float, float)>();
        foreach (string table in map.Tables is { Count: > 0 } ? map.Tables : new[] { map.Table })
        {
            if (_danger.TryGetValue(table, out var d))
            {
                guards.AddRange(d.Guards);
                elites.AddRange(d.Elites);
            }
        }

        return (guards.Distinct().ToList(), elites.Distinct().ToList());
    }

    private static Dictionary<string, (List<(float, float)> Guards, List<(float, float)> Elites)> LoadDanger()
    {
        var result = new Dictionary<string, (List<(float, float)>, List<(float, float)>)>();
        try
        {
            string path = AssetStore.Path("aion2", "pets", "farm_danger.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateObject())
                {
                    result[map.Name] = (Points(map.Value, "g"), Points(map.Value, "e"));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // no dangers known: the spots are worked out without them
        }

        return result;
    }

    private static List<(float, float)> Points(JsonElement map, string key) =>
        map.TryGetProperty(key, out var list) ? list.EnumerateArray().Select(p => ((float)p[0].GetDouble(), (float)p[1].GetDouble())).ToList() : new();
}
