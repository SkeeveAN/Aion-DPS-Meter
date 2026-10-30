using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The Hidden Cube arrivals (the count the map's chime and "show the map" follow) and the militia guards of the other side (Stacy's ideas, her fork 17 October 2026).</summary>
public static class SelfCheckCubesGuards
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Hidden Cube arrivals, militia guards:");
        var directory = new Aion2EntityDirectory();
        directory.NoteEnv(10, "Cube", "Hidden Cube", 0, 0);
        directory.NoteEnv(11, "Cube", "Hidden Cube", 500, 0);
        directory.NoteEnv(10, "Cube", "Hidden Cube", 0, 0);      // the same cube announced again: no new arrival
        directory.NoteEnv(12, "Herb", "Herb", 100, 100);         // a plant is no cube
        int two = directory.CubeArrivals;
        directory.ForgetEnv(10);
        directory.NoteEnv(10, "Cube", "Hidden Cube", 0, 0);      // gone and come back: an arrival again
        bool arrivals = two == 2 && directory.CubeArrivals == 3;

        // out of range (reason 1) keeps the cube remembered and counts a departure; opened or gathered (reason 4) removes it; back in sight is an arrival again
        directory.ForgetEnv(11, 1);
        bool left = directory.CubeDepartures == 1 && directory.LiveCubes().Count == 1 && directory.CubeDetails().Count == 2;
        directory.NoteEnv(11, "Cube", "Hidden Cube", 500, 0);
        left = left && directory.CubeArrivals == 4 && directory.LiveCubes().Count == 2;
        directory.ForgetEnv(11, 4);
        left = left && directory.CubeDepartures == 1 && directory.CubeDetails().Count == 1;

        directory.NoteMobPosition(100, 2930819, 1000, 2000);     // a Guard
        directory.NoteMobPosition(101, 1170, 3000, 4000);        // a pet monster
        var guards = directory.LiveGuards();
        bool live = guards.Count == 1 && guards[0] == (1000f, 2000f);
        directory.ForgetLiveMob(100);
        bool gone = directory.LiveGuards().Count == 0;

        bool ids = Enumerable.Range(2930813, 8).All(Aion2Guards.IsGuard) && Aion2Guards.IsGuard(2989448) && !Aion2Guards.IsGuard(2930812) && !Aion2Guards.IsGuard(2989449) && !Aion2Guards.IsGuard(1170);
        // hostile only on the open map of the other side: an Elyos on Altgard (World_D), an Asmodian on Verteron (World_L); never at home, in the Abyss, in an instance or when the own side is unknown
        bool hostile = Aion2Guards.IsHostileMap("World/World_D/World_D_A", "Elyos") && Aion2Guards.IsHostileMap("World/World_L/World_L_A", "Asmodian")
            && !Aion2Guards.IsHostileMap("World/World_L/World_L_A", "Elyos") && !Aion2Guards.IsHostileMap("World/World_D/World_D_A", "Asmodian")
            && !Aion2Guards.IsHostileMap("Intersever/Abyss/Abyss_Reshanta_A", "Elyos") && !Aion2Guards.IsHostileMap("Dungeon/Fire_Temple", "Elyos")
            && !Aion2Guards.IsHostileMap("World/World_D/World_D_A", null);
        Console.WriteLine($"  -> cube arrivals counted once per cube={arrivals}, out of sight remembered={left}, guards among the live monsters={live}/gone again={gone}, the 29 Guard NPC ids={ids}, hostile maps only={hostile}");
        bool ok = arrivals && left && live && gone && ids && hostile;
        Console.WriteLine(ok ? "  -> OK" : "  -> FAILED");
        return ok;
    }
}
