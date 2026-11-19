using AionDPS.Aion2;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The PvP alerts of the Abyss (Ui/PvpWatch.cs): an enemy is a player of the other faction, the first one in view sounds once, an enemy marking the player sounds (10 s per player),
/// party members and everything out of the Abyss stay silent. Built from what the lower Abyss recording of 2026-11-18 showed (the faction byte of the first-sight frame, the target frame).</summary>
public static class SelfCheckPvp
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] PvP alerts (Abyss, other faction = enemy):");
        var entities = new Aion2EntityDirectory();
        entities.SetLocalCharacter(new Aion2CharacterInfo(900, "Me", 21, 45, Array.Empty<Aion2EquippedItem>(), DateTime.UtcNow, ServerId: 1304));
        entities.Register(1001, "Friend");
        entities.NoteFaction(1001, 1);
        entities.Register(1002, "Enemy");
        entities.NoteFaction(1002, 2);
        var watch = new PvpWatch();
        DateTime t = new(2026, 11, 18, 20, 40, 0, DateTimeKind.Utc);
        bool ok = true;

        // out of the Abyss nothing sounds, even with an enemy in view
        entities.NoteAppearance(1002);
        bool quiet = watch.Poll(entities, 1010, t, true, true).Count == 0 && !watch.Active;
        Console.WriteLine($"  -> no alert outside the Abyss: {quiet}");
        ok &= quiet;

        // just came into the Abyss: what is in view is no news; once nobody has been in view for a second the watch is armed
        entities.NotePlayerGone(1002);
        watch.Poll(entities, 20, t, true, true);
        watch.Poll(entities, 20, t.AddSeconds(2), true, true);
        entities.NoteAppearance(1001); // a friend: no sound
        bool friend = watch.Poll(entities, 20, t.AddSeconds(3), true, true).Count == 0;
        entities.NoteAppearance(1002);
        var near = watch.Poll(entities, 20, t.AddSeconds(4), true, true);
        bool enemyNear = near.Count == 1 && near[0].Kind == PvpAlertKind.EnemyNear && near[0].Name == "Enemy";
        bool once = watch.Poll(entities, 20, t.AddSeconds(5), true, true).Count == 0; // he stays in view: no second sound
        Console.WriteLine($"  -> a friend makes no sound ({friend}), the first enemy one ({enemyNear}), the same enemy staying in view none ({once})");
        ok &= friend && enemyNear && once;

        // the enemy marks the local player: one sound, not again within 10 s; a friend marking him is none
        entities.NoteActorTarget(1001, 900, t.AddSeconds(6));
        entities.NoteActorTarget(1002, 900, t.AddSeconds(6));
        var marked = watch.Poll(entities, 20, t.AddSeconds(6), true, true);
        entities.NoteActorTarget(1002, 900, t.AddSeconds(9));
        bool again = watch.Poll(entities, 20, t.AddSeconds(9), true, true).Count == 0;
        entities.NoteActorTarget(1002, 900, t.AddSeconds(17));
        bool later = watch.Poll(entities, 20, t.AddSeconds(17), true, true).Count == 1;
        bool targeted = marked.Count == 1 && marked[0].Kind == PvpAlertKind.TargetedByEnemy && marked[0].Name == "Enemy";
        Console.WriteLine($"  -> the enemy marking the player: one sound ({targeted}), none within 10 s ({again}), one after ({later})");
        ok &= targeted && again && later;

        // the options switch each sound off
        entities.NoteActorTarget(1002, 900, t.AddSeconds(40));
        bool off = watch.Poll(entities, 20, t.AddSeconds(40), false, false).Count == 0;
        Console.WriteLine($"  -> both options off: no sound: {off}");
        ok &= off;

        // the player is gone, the zone changes: nobody is in view any more
        entities.NotePlayerGone(1002);
        entities.NoteAppearance(1002);
        entities.NoteZone(20);
        entities.NoteZone(23);
        bool cleared = entities.HostilePlayersInView().Count == 0;
        Console.WriteLine($"  -> a change of zone clears the view: {cleared}");
        ok &= cleared;
        return ok;
    }
}
