using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The replay map: position frames of the debug recordings, the walks kept without jumps, the size of a boss skill's area learnt from who it hit, and the replay put together.</summary>
public static class SelfCheckReplay
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Replay map (positions, areas learnt, the replay itself):");
        var entities = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), entities);
        DateTime at = new(2026, 10, 13, 20, 0, 0);
        bool ok = true;

        // real position frames of other entities: the entity, then the place in centimetres (the offset differs by family)
        decoder.Decode(Convert.FromHexString("1A37804F0000A6823B479193B546000C0347B35C"), at);
        decoder.Decode(Convert.FromHexString("1F37D11302294C854758C39B4640B7F9464449F7A2EFC2A6A6B4440FF07CC401"), at);
        decoder.Decode(Convert.FromHexString("3C37D19A010376D23A477104B046004003471446E9C2070201A4AAF500EFA6F65FA403"), at);
        var a = entities.Combat.PositionAt(10112, at);
        var b = entities.Combat.PositionAt(2513, at);
        var c = entities.Combat.PositionAt(19793, at);
        bool frames = a is { } pa && Math.Abs(pa.X - 48002.65) < 1 && Math.Abs(pa.Y - 23241.78) < 1
            && b is { } pb && Math.Abs(pb.X - 68249.3) < 2 && Math.Abs(pb.Y - 19937.67) < 2
            && c is { } pc && Math.Abs(pc.X - 47826.46) < 1 && Math.Abs(pc.Y - 22530.22) < 1;
        Console.WriteLine($"  -> position frames of three families give the place of entities that are not the local player: {frames}");
        ok &= frames;

        // a wrong reading is no jump; a real one counts once the next reading agrees
        var state = new Aion2CombatState();
        state.NotePosition(at, 7, 0, 100);
        state.NotePosition(at.AddMilliseconds(400), 7, 200, 100);
        state.NotePosition(at.AddMilliseconds(800), 7, 50000, 50000); // a wrong reading
        state.NotePosition(at.AddMilliseconds(1200), 7, 400, 100);
        state.NotePosition(at.AddMilliseconds(1600), 7, 60000, 100); // a jump ...
        state.NotePosition(at.AddMilliseconds(2000), 7, 60100, 100); // ... that the next reading confirms
        FightExtras kept = state.Slice(at, at.AddSeconds(3), _ => "x", _ => true);
        var xs = kept.Tracks!.Single().X;
        bool jumps = xs.SequenceEqual(new[] { 0, 200, 400, 60000, 60100 });
        Console.WriteLine($"  -> a wrong reading (50,000 m away for a moment) is dropped, a real jump kept once confirmed: {jumps}");
        ok &= jumps;

        // the area of a boss skill: how far from where it was wound up the players stood that it hit
        const int boss = 100, skill = 1236440;
        state = new Aion2CombatState();
        int[] reaches = { 300, 350, 400, 450, 500, 320, 380 };
        for (int i = 0; i < reaches.Length; i++)
        {
            DateTime t0 = at.AddSeconds(i * 10);
            state.NotePosition(t0, 1, 1000 + reaches[i], 1000);
            state.NotePosition(t0.AddSeconds(1.5), 1, 1000 + reaches[i], 1000);
            state.NoteWindup(new CastNotice(boss, skill, 1, t0, 1000, 0, 1000, 1000, 0, true));
            state.NoteHit(t0.AddMilliseconds(1300), boss, 1, skill, 500, targetIsPlayer: true, targetIsLocal: false);
        }

        int? radius = state.LessonOf(skill)?.RadiusCm;
        bool area = radius is int r && r is > 400 and < 700;
        Console.WriteLine($"  -> the area of a skill is learnt from where its hits landed (radius {radius} cm for hits at 3-5 m): {area}");
        ok &= area;

        // the replay put together
        var track = new PositionTrack(1, at, new[] { 0, 5000, 10000 }, new[] { 0, 1000, 1000 }, new[] { 0, 0, 1000 });
        var bossTrack = new PositionTrack(boss, at, new[] { 0, 10000 }, new[] { 500, 500 }, new[] { 500, 500 });
        var extras = new FightExtras(Array.Empty<SkillUse>(), Array.Empty<BuffEvent>(), new[] { new StaggerEvent(at.AddSeconds(8), boss, 0, 0, true) }, new Dictionary<int, string>(),
            new[] { track, bossTrack }, new[] { new WindupRecord(at.AddSeconds(2), boss, skill, 1, 1500, 90, 500, 500, true) }, new[] { new DeathRecord(at.AddSeconds(9), 1) }, null);
        var hits = new[] { new DamageEvent(at.AddSeconds(3.7), boss, 1, 800, false, "Slam", SkillId: skill) };
        ReplayData data = ReplayBuilder.Build("Test", extras, hits, at, at.AddSeconds(10), id => id == boss ? ("Boss", "", false) : ("Me", "Cleric", true), _ => 650, _ => "Slam");
        var entity = data.Entities.Single(e => e.Id == 1);
        bool halfway = entity.At(at, 2.5) is { } mid && Math.Abs(mid.X - 500) < 0.1 && Math.Abs(mid.Y) < 0.1; // between the readings at 0 s and 5 s
        bool put = data.HasData && data.Entities.Count == 2 && data.Areas.Count == 1 && data.Areas[0].RadiusCm == 650 && Math.Abs(data.Areas[0].End - 3.5) < 0.01
            && data.Hits.Count == 1 && data.Deaths.Count == 1 && data.Staggers.Count == 1 && halfway;
        Console.WriteLine($"  -> a walk is drawn straight between two readings ({halfway}); the replay has the group, the boss, its area, the hit, the death and the stagger: {put}");
        ok &= put;

        // another monster the group fought is an "add": a marker of its own, no area, no health
        const int add = 300;
        var addTrack = new PositionTrack(add, at, new[] { 0, 4000 }, new[] { 100, 300 }, new[] { 100, 100 });
        var withAdd = extras with { Tracks = new[] { track, bossTrack, addTrack } };
        ReplayData addData = ReplayBuilder.Build("Test", withAdd, hits, at, at.AddSeconds(10), id => id == boss ? ("Boss", "", false) : id == add ? ("Imp", "", false) : ("Me", "Cleric", true), _ => 650, _ => "Slam", id => id == add);
        bool adds = addData.Entities.Single(e => e.Id == add).Role == ReplayRole.Add && addData.Entities.Single(e => e.Id == boss).Role == ReplayRole.Boss && addData.Areas.Count == 1;
        Console.WriteLine($"  -> another monster of the fight is an add (no area of its own, the boss stays the boss): {adds}");
        ok &= adds;

        // an add that never moved has one point (where it appeared): it stands there until it dies
        const int still = 301;
        var stillTrack = new PositionTrack(still, at.AddSeconds(-20), new[] { 0 }, new[] { 700 }, new[] { 300 });
        var withStill = extras with { Tracks = new[] { track, bossTrack, stillTrack }, Deaths = new[] { new DeathRecord(at.AddSeconds(6), still) } };
        ReplayData stillData = ReplayBuilder.Build("Test", withStill, hits, at, at.AddSeconds(10), id => id == boss ? ("Boss", "", false) : id == still ? ("Imp", "", false) : ("Me", "Cleric", true), _ => 650, _ => "Slam", id => id == still);
        ReplayEntity imp = stillData.Entities.Single(e => e.Id == still);
        bool stays = imp.Static && imp.At(at, 1) is { X: 700 } && imp.At(at, 5.9) is { Y: 300 } && imp.At(at, 6.1) is null && stillData.Entities.Count == 3;
        Console.WriteLine($"  -> a monster that never moved stands where it appeared until it dies: {stays}");
        ok &= stays;

        // the own character: between two readings (0 s and 5 s) the spot is an estimate, within 1.5 s of one it is not, and nobody else's ever is
        ReplayData selfData = ReplayBuilder.Build("Test", extras, hits, at, at.AddSeconds(10), id => id == boss ? ("Boss", "", false) : ("Me", "Cleric", true), _ => 650, _ => "Slam", null, id => id == 1);
        ReplayEntity me = selfData.Entities.Single(e => e.Id == 1);
        bool estimates = me.IsSelf && me.EstimatedAt(at, 2.5) && !me.EstimatedAt(at, 0.5) && !me.EstimatedAt(at, 4.0) && !me.EstimatedAt(at, 5) && !selfData.Entities.Single(e => e.Id == boss).EstimatedAt(at, 2.5);
        Console.WriteLine($"  -> the own character's spot between two readings is marked as an estimate: {estimates}");
        ok &= estimates;
        return ok;
    }
}
