using System.IO;
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

        // the history file keeps them: an old row without the column still loads, a new one brings its extras back
        string path = Path.Combine(Path.GetTempPath(), "aiondps-rotation-selftest-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var store = new FightStore(path))
            {
                var summary = new FightSummary(0, "aion2", null, start, end, "Boss", "pve", 1000, 1, "Me");
                var detail = new FightDetail(summary, new[] { new FightParticipant("Me", "Gladiator", "", true, false, 1000, 50, 0, 0) }, damage,
                    new Dictionary<int, string> { [you] = "Me", [boss] = "Boss" }, extras);
                long id = store.Insert(detail);
                FightDetail? back = store.Load(id);
                bool kept = back?.Extras is { } e && e.Uses.Count == 6 && e.Buffs.Count == 3 && e.Staggers.Count == 1 && e.Names[other] == "Wendy" && e.Uses[0].At == start;
                Console.WriteLine($"  -> the extras come back from the history file: {kept}");
                ok &= kept;
            }
        }
        finally
        {
            foreach (string suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(path + suffix); } catch (IOException) { }
            }
        }

        return ok;
    }
}
