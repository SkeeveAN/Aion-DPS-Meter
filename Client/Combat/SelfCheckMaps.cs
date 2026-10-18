using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The maps of the pet map: the open maps by their zone, the instance maps by the zone id of the instance, and the world -> map mapping of an instance.</summary>
public static class SelfCheckMaps
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Maps (open world and instances):");
        bool open = Aion2Maps.OfZone(1010)?.Key == "verteron" && Aion2Maps.OfZone(1110)?.Key == "altgard" && Aion2Maps.All.Count == 3;
        Console.WriteLine($"  -> open maps by zone: {open}; the instances are not part of the open maps: {Aion2Maps.All.All(m => !m.Instance)}");

        // zone ids of instances: the Ferocious Horn Den (600091..93) and the Dungeon_L_0019 (210019); a zone the files do not know has no map
        var horn = Aion2Maps.OfZone(600092);
        bool zones = horn?.Key == "HornCave" && horn.Instance && Aion2Maps.OfZone(210019)?.Key == "Dungeon_L_0019" && Aion2Maps.OfZone(600999) is null;
        Console.WriteLine($"  -> instance zones: Horn Den {horn?.Key}, Dungeon_L_0019 {Aion2Maps.OfZone(210019)?.Key}, unknown zone none: {zones}");

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
        return open && zones && mapped && points;
    }
}
