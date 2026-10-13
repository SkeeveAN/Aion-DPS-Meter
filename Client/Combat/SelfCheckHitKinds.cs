using AionDPS.Aion2;
using AionDPS.Aion2.Protocol;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>How a hit landed (double, perfect, parry, back, front, several strikes), read from the real hit frames of the debug recordings of 2026-10-12.</summary>
public static class SelfCheckHitKinds
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Hit kinds (double, perfect, parry, back, front, multi):");
        var entities = new Aion2EntityDirectory();
        var decoder = new Aion2FrameDecoder(Aion2Protocol.Load(), entities);
        DateTime at = new(2026, 10, 13, 20, 0, 0);
        DamageEvent Hit(string hex) => decoder.Decode(Convert.FromHexString(hex), at).Single();

        // the first one is a heal (kind 4, its own skill family): a heal carries no hit kinds
        var heal = Hit("0438E08D030400E08D03343F030101025BB04465010000008A55BF5B0100");
        var front = Hit("0438D294010600BB01D6C62D009302000002A3ABE111010000009E55C6040100");
        var back = Hit("0438D196030600D19A01A4AAF5000203000001F9A6F65F02000000D653E31A0200");
        var perfect = Hit("04389093010600AB71E61517012003040002D466046D02000000EE6482D9020200");
        var dbl = Hit("0438FDDF020600AB71424513013203080002C3E6866B01000000EE64986F0100");
        var parry = Hit("0438AB710600909301829312000302020002D39E410701000000904EC0010100");
        var multi = Hit("0438B6280400B628B272BC0007028DA69C4903000000FE5D970E0300");

        bool basics = heal.IsHeal && heal.Kinds is null
            && front.Amount == 582 && front.Kinds == HitKinds.Front && !front.IsCritical
            && back.Amount == 3427 && back.Kinds == HitKinds.Back && back.IsCritical && back.HitCount == 2;
        bool flags = perfect.Amount == 44162 && perfect.Kinds == (HitKinds.Perfect | HitKinds.Front) && perfect.IsCritical
            && dbl.Amount == 14232 && dbl.Kinds == (HitKinds.Double | HitKinds.Front)
            && parry.Amount == 192 && parry.Kinds == (HitKinds.Parry | HitKinds.Front)
            && multi.Amount == 1815 && multi.HitCount == 3 && multi.Kinds == HitKinds.None;
        Console.WriteLine($"  -> a heal without kinds, a front hit, a back crit with two strikes and a multi hit with three read as they are: {basics && multi.HitCount == 3}");
        Console.WriteLine($"  -> perfect, double and parry are told apart from the direction: {flags}");

        // they go with the fight into the history file; a hit stored before has none
        var stored = EventBlob.Unpack(EventBlob.Pack(new[] { back, front with { Kinds = null } }, new Dictionary<int, string>())).Events;
        bool roundTrip = stored[0].Kinds == HitKinds.Back && stored[0].HitCount == 2 && stored[1].Kinds is null && stored[1].HitCount == 1;
        Console.WriteLine($"  -> the hit kinds survive the history file, and a hit stored without them stays 'unknown': {roundTrip}");

        // rates per skill, over the hits that said anything
        var events = new List<DamageEvent>();
        for (int i = 0; i < 40; i++)
        {
            bool isDouble = i % 4 == 0;
            events.Add(new DamageEvent(at.AddSeconds(i), 1, 100, isDouble ? 2000 : 1000, false, "Cleave", Kinds: isDouble ? HitKinds.Double : HitKinds.Front));
        }

        events.Add(new DamageEvent(at, 1, 100, 500, false, "Cleave")); // no data: not in the rates
        var counts = HitKindCounts.Of(events);
        var effects = HitKindStats.Effects(events);
        var doubleEffect = effects.Single(e => e.Name == "Double");
        bool rates = counts.Known == 40 && counts.Double == 10 && Math.Abs(counts.Rate(counts.Double) - 25) < 0.01 && Math.Abs(doubleEffect.MedianRatio - 2.0) < 0.01 && doubleEffect.SkillsCompared == 1;
        Console.WriteLine($"  -> the share of a skill's hits is over the hits that said anything (25 % double), and a double hit that did twice the damage reads as x2: {rates}");
        return basics && multi.HitCount == 3 && flags && roundTrip && rates;
    }
}
