using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>The frames behind the combat overlay (casts, whom an entity aims at, effects, maximum hit points), as the game sent
/// them in the debug recordings of 2026-10-12 - the bytes are copied from there.</summary>
public static class SelfCheckCombatOverlay
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    public static bool Run()
    {
        Console.WriteLine("[selftest] Combat overlay frames (windup, cast, effects, maximum hit points):");
        var entities = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), entities);
        DateTime at = new(2026, 10, 12, 21, 3, 6, 187);
        bool ok = true;

        // a monster winds up a skill: 1,500 ms, aimed at entity 41,205
        decoder.Decode(Hex("0938C8950102D8DD12000102F5C10246E29042934E3F4705C6B846001D0347DC0B01904E"), at);
        var casts = entities.Combat.Casts(at.AddMilliseconds(200), TimeSpan.FromSeconds(1));
        bool windup = casts.Count == 1 && casts[0].Notice.Actor == 19144 && casts[0].Notice.SkillId == 1236440
            && casts[0].Notice.Target == 41205 && casts[0].Notice.DurationMs == 1500 && casts[0].Notice.HasPosition && Math.Abs(casts[0].Notice.Facing - 72.44) < 0.1;
        Console.WriteLine($"  -> windup: caster 19144, skill 1236440, aimed at 41205, 1500 ms, with a position: {windup}");
        ok &= windup;

        bool over = entities.Combat.Casts(at.AddMilliseconds(1500 + 4500), TimeSpan.FromSeconds(1)).Count == 0;
        Console.WriteLine($"  -> the cast is gone a few seconds after it ended: {over}");
        ok &= over;

        // a player's skill cast: remembered as whom the player aims at
        DateTime castAt = at.AddSeconds(1);
        decoder.Decode(Hex("0238FC4500E5A709010E00FC45B6B60442EE550100"), castAt);
        bool aim = entities.Combat.TargetOf(8956, castAt.AddSeconds(1), TimeSpan.FromSeconds(6)) == 8956
            && entities.Combat.TargetOf(8956, castAt.AddSeconds(30), TimeSpan.FromSeconds(6)) is null;
        Console.WriteLine($"  -> a cast notice gives the caster's target for a few seconds only: {aim}");
        ok &= aim;

        // an effect on entity 5,174 (instance 55, effect 200 = skill 20, 300 ms), then one that ends
        decoder.Decode(Hex("2A38B6280111" + "37" + "C8000000" + "2C010000" + "00000000" + "7E62572CA1010000" + "B628" + "0100" + "C8FB3A47" + "3411B446" + "001B0347"), at);
        var buffs = entities.Combat.BuffsOn(5174, at.AddMilliseconds(100));
        bool on = buffs.Count == 1 && buffs[0].Instance == 55 && buffs[0].SkillId == 20 && buffs[0].DurationMs == 300 && buffs[0].Caster == 5174;
        bool expired = entities.Combat.BuffsOn(5174, at.AddMilliseconds(400)).Count == 0;
        Console.WriteLine($"  -> effect on 5174: instance 55, skill 20, 300 ms, from itself: {on}; gone after its time: {expired}");
        ok &= on && expired;

        entities.Combat.NoteBuff(new BuffNotice(at, 10112, 42, 100, 10, -1, 1));
        decoder.Decode(Hex("2C38804F01002A01"), at.AddSeconds(1));
        bool ended = entities.Combat.BuffsOn(10112, at.AddSeconds(2)).Count == 0;
        Console.WriteLine($"  -> an effect without an end stays until its end frame (instance 42 on 10112): {ended}");
        ok &= ended;


        // what the overlay makes of it: a monster that is no boss shows only when asked for; the target of the target and the healer's target follow the clicked target
        var settings = new AionDPS.Ui.MeterSettings();
        DateTime during = at.AddMilliseconds(400);
        bool hidden = AionDPS.Ui.CombatOverlayData.Casts(entities, settings, during, "en").Count == 0;
        settings.CombatCastsOfAllMonsters = true;
        var rows = AionDPS.Ui.CombatOverlayData.Casts(entities, settings, during, "en");
        bool shown = rows.Count == 1 && rows[0].CastMs == 1500 && rows[0].TargetText == "#41205" && !rows[0].AtYou;
        Console.WriteLine($"  -> a monster's cast shows on the bar only for \"all monsters\" ({rows.Count} row, aimed at #41205): {hidden && shown}");
        ok &= hidden && shown;

        entities.SetLocalTarget(19144);
        var tot = AionDPS.Ui.CombatOverlayData.TargetOfTarget(entities, new AionDPS.Ui.MeterSettings(), during, "en");
        bool totOk = tot is not null && tot.Name == "#41205" && tot.From == "#19144";
        entities.HitPoints.Note(19144, during, 6400);
        entities.HitPoints.NoteMax(19144, 10000);
        var healer = AionDPS.Ui.CombatOverlayData.Healer(entities, settings, during, "en");
        bool healerOk = healer is not null && healer.Hp == 6400 && healer.MaxHp == 10000;
        Console.WriteLine($"  -> clicked target 19144: its target is #41205: {totOk}; its health 6,400 of 10,000: {healerOk}");
        ok &= totOk && healerOk;

        decoder.Decode(Hex("008DD47B0201" + "07" + "B51C000000000000"), at);
        bool max = entities.HitPoints.MaxOf(15828) == 7349;
        Console.WriteLine($"  -> HP frame kind 7 is the maximum (7,349 on 15828): {max}");
        ok &= max;
        return ok;
    }
}
