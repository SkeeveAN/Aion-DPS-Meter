using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The maps of the pet map: the open maps by their zone, the instance maps by the zone id of the instance, and the world -> map mapping of an instance.</summary>
public static class SelfCheckMaps
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Maps (open world and instances):");
        bool open = Aion2Maps.OfZone(1010)?.Key == "verteron" && Aion2Maps.OfZone(1110)?.Key == "altgard" && Aion2Maps.All.Count == 4;
        Console.WriteLine($"  -> open maps by zone: {open}; the instances are not part of the open maps: {Aion2Maps.All.All(m => !m.Instance)}");

        // the Lower Abyss (A) and the Outer Abyss (D, open since 24 November 2026): their own maps with the names the user chose, the spawn points of the pet monsters on land
        var lower = Aion2Maps.All.FirstOrDefault(m => m.Key == "abyss");
        var outer = Aion2Maps.All.FirstOrDefault(m => m.Key == "abyss_outer");
        int onMap = 0, onLand = 0;
        if (outer is not null)
        {
            foreach ((int _, float x, float y) in Aion2Maps.PointsOf(outer, Aion2Pets.AllPetIds.ToHashSet()))
            {
                (double px, double py) = Aion2Maps.ToNative(outer, x, y);
                onMap += px > 0 && py > 0 && px < outer.Tile * outer.Grid && py < outer.Tile * outer.Grid ? 1 : 0;
                onLand += 1;
            }
        }

        bool abyss = lower?.NameIn("de") == "Unteres Abyss" && outer?.NameIn("de") == "Äußeres Abyss" && outer.Table.EndsWith("Abyss_Reshanta_D", StringComparison.Ordinal) && onLand > 500 && onMap == onLand;
        Console.WriteLine($"  -> Lower Abyss '{lower?.NameIn("de")}', Outer Abyss '{outer?.NameIn("de")}' with {onLand} pet spawn points, all inside the map: {abyss}");

        // zone ids of instances: the Ferocious Horn Den (600091..93) and the Dungeon_L_0010 (210010); a zone the files do not know has no map
        var horn = Aion2Maps.OfZone(600092);
        bool zones = horn?.Key == "HornCave" && horn.Instance && Aion2Maps.OfZone(210010)?.Key == "Dungeon_L_0010" && Aion2Maps.OfZone(600999) is null && Aion2Maps.OfZone(210019) is null;
        Console.WriteLine($"  -> instance zones: Horn Den {horn?.Key}, Dungeon_L_0010 {Aion2Maps.OfZone(210010)?.Key}, unknown zone none, an instance without pet monsters (Dungeon_L_0019) has no map: {zones}");

        // the world -> map pixel: a monster of the Horn Den (about (-5225, 10748)) lies on the floor mask of the map (the build's own check found 85 % of its spawn points there)
        bool mapped = false;
        if (horn is not null)
        {
            (double px, double py) = Aion2Maps.ToNative(horn, -5225, 10748);
            double whole = horn.Tile * horn.Grid;
            mapped = px > 0 && py > 0 && px < whole && py < whole;
            Console.WriteLine($"  -> Horn Den world (-5225, 10748) -> pixel ({px:0}, {py:0}) of {whole:0}: inside {mapped}");
        }

        // the pet monsters of an instance: its spawn tables (one per difficulty) give the points, a place of two difficulties once
        bool points = horn is not null && Aion2Maps.PointsOf(horn, Aion2Pets.AllPetIds.ToHashSet()).Count > 0;
        Console.WriteLine($"  -> pet spawn points in the Horn Den: {points}");
        return open && abyss && zones && mapped && points;
    }
}
