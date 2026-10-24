using AionDPS.Aion2.Protocol;
using AionDPS.History;

namespace AionDPS.Combat;

/// <summary>The rotation, effect and stagger views: weaves and counting, an effect two givers kept up counts once, stagger phases, and the extras surviving the history file.</summary>
public static class SelfCheckRotation
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Rotation, effects and stagger of a fight:");
        const int you = 1, other = 2, boss = 100;
        DateTime start = new(2026, 10, 13, 20, 0, 0);
        DateTime end = start.AddSeconds(20);
        int skillA = 17278949, skillB = 11000100, buffSkill = 17000010;
        string nameA = Aion2SkillNames.NameOf(skillA), nameB = Aion2SkillNames.NameOf(skillB);

        var uses = new List<SkillUse>
        {
            new(start, you, skillA), new(start.AddSeconds(0.2), you, skillB), new(start.AddSeconds(0.3), you, skillA),
            new(start.AddSeconds(5), you, skillA), new(start.AddSeconds(5.1), you, skillB),
            new(start.AddSeconds(6), other, skillA), // someone else's
        };
        var buffs = new List<BuffEvent>
        {
            new(start, BuffKind.On, you, 1, buffSkill, other, -1),
            new(start.AddSeconds(5), BuffKind.On, you, 2, buffSkill, you, 5000),
            new(start.AddSeconds(10), BuffKind.End, you, 1, 0, 0, 0),
        };
        var staggers = new List<StaggerEvent> { new(start.AddSeconds(3), boss, 0, 0, true) };
        var extras = new FightExtras(uses, buffs, staggers, new Dictionary<int, string> { [other] = "Wendy", [you] = "Me" });
        var damage = new List<DamageEvent> { new(start.AddSeconds(1), you, boss, 600, false, nameA), new(start.AddSeconds(2), you, boss, 400, false, nameB) };

        RotationReport report = RotationAnalysis.Build(extras, you, start, end, damage, id => extras.Names.GetValueOrDefault(id), _ => false);
        bool rows = report.Uses.Count == 5 && report.Uses.Select(u => u.Row).SequenceEqual(new[] { 0, 1, 2, 0, 1 });
        bool counted = report.Uses.Count(u => u.Name == nameA) == 3 && report.Uses[^1].Number == 2 && report.Uses[^1].OfTotal == 2 && report.Uses[3].Number == 3 && report.Uses[3].OfTotal == 3;
        RotationSkill? first = report.Skills.FirstOrDefault(s => s.Name == Aion2SkillNames.Display(nameA));
        bool shares = first is not null && first.Uses == 3 && Math.Abs(first.SharePercent - 60) < 0.01;
        Console.WriteLine($"  -> weaves one row lower (0,1,2 then a pause, 0,1), other players' skills left out: {rows}");
        Console.WriteLine($"  -> uses counted per skill, share of the damage 60 %: {counted && shares}");

        BuffRow? row = report.Buffs.FirstOrDefault();
        bool union = row is not null && Math.Abs(row.UptimePercent - 50) < 0.01 && Math.Abs(row.FromYouPercent - 25) < 0.01 && row.Applications == 2;
        Console.WriteLine($"  -> an effect two givers kept up at once counts once (10 s of 20 = 50 %, 25 % from you, 2 times): {union}");

        bool stagger = report.Staggers.Count == 1 && Math.Abs(report.Staggers[0].Start - 3) < 0.01 && Math.Abs(report.Staggers[0].End - 8) < 0.01;
        Console.WriteLine($"  -> a stagger break is a band from 3 s to 8 s: {stagger}");

        bool ok = rows && counted && shares && union && stagger;

        // they can be packed and read back (saved with a session)
        FightExtras back = ExtrasBlob.Unpack(ExtrasBlob.Pack(extras));
        bool kept = back.Uses.Count == 6 && back.Buffs.Count == 3 && back.Staggers.Count == 1 && back.Names[other] == "Wendy" && back.Uses[0].At == start;
        Console.WriteLine($"  -> the extras can be packed and read back: {kept}");
        ok &= kept;


        // deaths: counted once (the game repeats a 0 reading), with the moment of the revive and how long the player was dead
        DateTime t0 = new(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc);
        var readings = new List<(DateTime, long)> { (t0, 100), (t0.AddSeconds(5), 0), (t0.AddSeconds(6), 0), (t0.AddSeconds(30), 80), (t0.AddSeconds(60), 0) };
        List<Death> found = Deaths.Find(7, readings, new List<DamageEvent> { new(t0.AddSeconds(4), 100, 7, 999, false, "Smash") });
        bool deathsOk = found.Count == 2 && found[0].RevivedAt == t0.AddSeconds(30) && found[0].DeadFor(t0.AddMinutes(5)) == TimeSpan.FromSeconds(25)
            && found[0].KillingBlow is { Amount: 999 } && found[1].RevivedAt is null && found[1].DeadFor(t0.AddSeconds(90)) == TimeSpan.FromSeconds(30);
        var state = new Aion2.Aion2CombatState();
        state.NoteDeath(7, t0.AddSeconds(5));
        state.NoteDeath(7, t0.AddSeconds(6));
        state.NoteRevive(7, t0.AddSeconds(30));
        state.NoteDeath(7, t0.AddSeconds(60));
        List<DeathRecord> logged = state.DeathsIn(t0, t0.AddMinutes(2));
        FightExtras withDeaths = ExtrasBlob.Unpack(ExtrasBlob.Pack(new FightExtras(Array.Empty<SkillUse>(), Array.Empty<BuffEvent>(), Array.Empty<StaggerEvent>(), new Dictionary<int, string>(), null, null, logged)));
        bool logOk = logged.Count == 2 && logged[0].RevivedAt == t0.AddSeconds(30) && logged[1].RevivedAt is null
            && withDeaths.Deaths is { Count: 2 } restored && restored[0].RevivedAt == t0.AddSeconds(30) && restored[1].RevivedAt is null;
        Console.WriteLine($"  -> a death is counted once with its revive and the time dead (25 s), the file keeps it: {deathsOk && logOk}");
        ok &= deathsOk && logOk;

        // the killing blow: the death frame names the skill, so the latest hit with it counts, not the last hit of the seconds before
        var onePlayer = new List<(DateTime, long)> { (t0, 100), (t0.AddSeconds(5), 0) };
        var twoHits = new List<DamageEvent> { new(t0.AddSeconds(3), 100, 7, 500, false, "Smash", SkillId: 222), new(t0.AddSeconds(4.5), 100, 7, 40, false, "Poke", SkillId: 111) };
        var named = new List<Aion2.KillCredit> { new(t0.AddSeconds(5.05), 222) };
        var unknown = new List<Aion2.KillCredit> { new(t0.AddSeconds(5.05), 999) };
        bool plain = Deaths.Find(7, onePlayer, twoHits)[0].KillingBlow is { SkillId: 111 };
        bool byName = Deaths.Find(7, onePlayer, twoHits, named)[0].KillingBlow is { SkillId: 222 };
        bool fallback = Deaths.Find(7, onePlayer, twoHits, unknown)[0].KillingBlow is { SkillId: 111 };
        bool tooLate = Deaths.Find(7, onePlayer, twoHits, new List<Aion2.KillCredit> { new(t0.AddSeconds(9), 222) })[0].KillingBlow is { SkillId: 111 };
        var hp = new Aion2.Aion2HitPoints();
        hp.NoteKillCredit(7, 222, t0.AddSeconds(5.05));
        bool stored = hp.KillCreditsBetween(7, t0, t0.AddSeconds(6)).Count == 1 && hp.KillCreditsBetween(7, t0.AddSeconds(6), t0.AddSeconds(9)).Count == 0 && hp.KillCreditsBetween(8, t0, t0.AddSeconds(9)).Count == 0;
        Console.WriteLine($"  -> the killing blow is the hit with the skill the death frame names (else the last hit): {plain && byName && fallback && tooLate && stored}");
        ok &= plain && byName && fallback && tooLate && stored;
        return ok;
    }
}
