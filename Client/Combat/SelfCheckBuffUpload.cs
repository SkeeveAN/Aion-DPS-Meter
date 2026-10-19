using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>The buffs of the upload: uptime and givers per effect, potions and a monster's effects left out.</summary>
public static class SelfCheckBuffUpload
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Buffs of the upload:");
        const int you = 1, wendy = 2, boss = 100;
        DateTime start = new(2026, 10, 19, 20, 0, 0);
        DateTime end = start.AddSeconds(20);
        const int light = 17410011, scroll = 2210103, potion = 2011101, monsterEffect = 1801692;
        var buffs = new List<BuffEvent>
        {
            new(start, BuffKind.On, you, 1, light, wendy, -1),
            new(start.AddSeconds(10), BuffKind.End, you, 1, 0, 0, 0),
            new(start, BuffKind.On, you, 2, scroll, you, -1),
            new(start, BuffKind.On, you, 3, potion, you, -1),
            new(start, BuffKind.On, you, 4, monsterEffect, boss, -1),
        };
        var names = new Dictionary<int, string> { [you] = "Me", [wendy] = "Wendy", [boss] = "Boss" };
        var extras = new FightExtras(new List<SkillUse> { new(start, you, 11000100), new(start, wendy, 17000010) }, buffs, new List<StaggerEvent>(), names);

        IReadOnlyList<BuffUsageUpload> list = BuffUpload.For(extras, you, start, end, id => names.GetValueOrDefault(id));
        BuffUsageUpload? lightRow = list.FirstOrDefault(b => b.SkillId == light);
        BuffUsageUpload? scrollRow = list.FirstOrDefault(b => b.SkillId == scroll);
        bool uptime = lightRow is not null && Math.Abs(lightRow.UptimePermille - 500) <= 5 && scrollRow is not null && scrollRow.UptimePermille >= 995;
        bool givers = lightRow?.Givers is { Count: 1 } g && g[0] == "Wendy" && scrollRow?.Givers is { Count: 0 };
        bool noPotion = list.All(b => b.SkillId != potion);
        Console.WriteLine($"  -> an effect from another player: 50 % of the fight, giver named; an own scroll: whole fight, no giver: {uptime && givers}");
        Console.WriteLine($"  -> potions are left out: {noPotion}");

        var kept = BuffUpload.KeepGroupSources(list, new HashSet<string> { "Me", "Wendy" });
        bool monster = list.Any(b => b.SkillId == monsterEffect) && kept.All(b => b.SkillId != monsterEffect) && kept.Any(b => b.SkillId == light);
        Console.WriteLine($"  -> what a monster gave is dropped, the group's buff stays: {monster}");
        return uptime && givers && noPotion && monster;
    }
}
