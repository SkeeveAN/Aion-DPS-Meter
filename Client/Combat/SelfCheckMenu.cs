using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The game-menu detection by the mouse cursor: a menu hides the overlays, the show-cursor key does not, and the control mode is learnt from fights.</summary>
public static class SelfCheckMenu
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Game menu detection (mouse cursor):");
        DateTime t = new(2026, 10, 13, 20, 0, 0);
        TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

        // a menu opens: the cursor shows for a moment with nothing held, then it counts; it counts a moment after the cursor hides, too
        var d = new MenuDetector();
        bool early = !d.Update(t, true, true, false, false) && !d.Update(t + Ms(100), true, true, false, false);
        bool menu = d.Update(t + Ms(300), true, true, false, false);
        bool lingers = d.Update(t + Ms(2000), true, false, false, false) && !d.Update(t + Ms(2400), true, false, false, false);
        Console.WriteLine($"  -> a menu counts after a quarter second of cursor ({early && menu}) and ends a moment after it hides ({lingers})");

        // keys and clicks inside a menu change nothing; a click makes the menu state last longer while the cursor flickers
        d = new MenuDetector();
        d.Update(t, true, true, false, false);
        d.Update(t + Ms(300), true, true, false, false);
        bool clickLinger = d.Update(t + Ms(1200), true, false, false, false, t + Ms(1000)) && !d.Update(t + Ms(3000), true, false, false, false, t + Ms(1000));
        Console.WriteLine($"  -> after a click the menu state lasts longer while the cursor is swapped out: {clickLinger}");

        // the show-cursor key (Alt) shows the cursor without a menu: the meter stays; a key pressed inside an open menu changes nothing
        d = new MenuDetector();
        bool peek = true;
        for (double sec = 0; sec < 19.5; sec += 0.5)
        {
            peek &= !d.Update(t + TimeSpan.FromSeconds(sec), true, true, true, false);
        }

        bool stuck = d.Update(t + TimeSpan.FromSeconds(20.6), true, true, true, false);
        d = new MenuDetector();
        d.Update(t, true, true, false, false);
        d.Update(t + Ms(300), true, true, false, false);
        bool keyInMenu = d.Update(t + Ms(600), true, true, true, false);
        Console.WriteLine($"  -> holding the show-cursor key keeps the meter up ({peek}); stuck after 20 s ({stuck}); a key inside an open menu changes nothing ({keyInMenu})");

        // other windows in front, or the meter's own overlays being placed: never a menu
        d = new MenuDetector();
        d.Update(t, true, true, false, false);
        bool away = !d.Update(t + Ms(500), false, true, false, false);
        d.Update(t + Ms(600), true, true, false, false);
        bool placing = !d.Update(t + Ms(1000), true, true, false, true);
        Console.WriteLine($"  -> not with another window in front ({away}), not while overlays are being placed ({placing})");

        // the control mode, learnt while dealing damage
        var learner = new ControlModeLearner();
        for (int i = 0; i <= 26; i++)
        {
            learner.Note(t + TimeSpan.FromSeconds(i * 0.5), true, true, true);
        }

        bool classic = learner.Mode == ControlMode.Classic;
        learner = new ControlModeLearner();
        for (int i = 0; i <= 32; i++)
        {
            learner.Note(t + TimeSpan.FromSeconds(i * 0.5), true, true, false);
        }

        bool hidden = learner.Mode == ControlMode.Hidden;
        learner = new ControlModeLearner();
        for (int i = 0; i <= 80; i++)
        {
            learner.Note(t + TimeSpan.FromSeconds(i * 0.5), true, false, true); // a menu open between fights teaches nothing
        }

        bool quiet = learner.Mode == ControlMode.Unknown;
        Console.WriteLine($"  -> cursor shown while fighting for 13 s = classic ({classic}); hidden for 16 s = hidden ({hidden}); no fight, nothing learnt ({quiet})");
        return early && menu && lingers && clickLinger && peek && stuck && keyInMenu && away && placing && classic && hidden && quiet;
    }
}
