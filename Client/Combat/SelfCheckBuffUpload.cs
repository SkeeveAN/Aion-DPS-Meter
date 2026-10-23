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

        // the spans: the effect from Wendy ran from 0 s to 10 s, the scroll the whole fight
        bool spans = lightRow?.Spans is { Count: 2 } ls && ls[0] == 0 && ls[1] == 100 && scrollRow?.Spans is { Count: 2 } ss && ss[0] == 0 && ss[1] >= 195;
        Console.WriteLine($"  -> the spans of an effect go along in tenths of a second: {spans}");
        var flicker = BuffUpload.SpansOf(new[] { (0.0, 1.0), (1.2, 2.0), (5.0, 6.0) });
        bool joined = flicker is { Count: 4 } f && f[0] == 0 && f[1] == 20 && f[2] == 50 && f[3] == 60;
        var many = Enumerable.Range(0, 500).Select(i => (i * 2.0, i * 2.0 + 1.0)).ToList();
        bool capped = BuffUpload.SpansOf(many) is { } m && m.Count <= BuffUpload.MaxSpans * 2 && m[0] == 0;
        Console.WriteLine($"  -> a flicker under half a second is one span, too many spans are joined to fit: {joined && capped}");

        // the effects on the boss: what Wendy put on it counts (with her as giver), what the boss gave itself does not
        var onBoss = new List<BuffEvent> { new(start, BuffKind.On, boss, 11, light, wendy, -1), new(start, BuffKind.On, boss, 12, monsterEffect, boss, -1) };
        IReadOnlyList<BuffUsageUpload>? debuffs = BuffUpload.OnBoss(extras with { Buffs = onBoss }, boss, start, end, id => names.GetValueOrDefault(id));
        bool bossEffects = debuffs is { Count: 1 } bd && bd[0].SkillId == light && bd[0].Givers is { Count: 1 } bg && bg[0] == "Wendy" && bd[0].UptimePermille >= 995;
        Console.WriteLine($"  -> the effects on the boss: what a player put on it, not what it gave itself: {bossEffects}");

        // the time axis: skills used (the passive and the duplicates by name aside), the death of the player, the boss's health
        var uses = new List<SkillUse> { new(start.AddSeconds(1), you, 11000100), new(start.AddSeconds(3.5), you, 11000100), new(start.AddSeconds(6), you, 11000200), new(start.AddSeconds(2), wendy, 17000010) };
        var deaths = new List<DeathRecord> { new(start.AddSeconds(8), you), new(start.AddSeconds(9), wendy) };
        var hp = new List<HpSeries> { new(boss, start, new[] { 0, 200, 400, 5000, 10_000 }, new short[] { 1000, 990, 985, 600, 300 }) };
        var withTime = extras with { Uses = uses, Deaths = deaths, Hp = hp };
        TimelineUpload? line = TimelineBuilder.For(withTime, you, start, end, id => names.GetValueOrDefault(id));
        bool axis = line is not null && line.Times.Count == 3 && line.Times[0] == 10 && line.Times[1] == 35 && line.Times[2] == 60 && line.Skills.Count == 2
            && line.Index[0] == line.Index[1] && line.Index[2] != line.Index[0] && line.Deaths.SequenceEqual(new[] { 80 });
        Console.WriteLine($"  -> the player's skill uses and death in tenths of a second, nobody else's: {axis}");
        IReadOnlyList<int>? curve = TimelineBuilder.BossHp(withTime, boss, start, end);
        bool health = curve is not null && curve.Count % 2 == 0 && curve[0] == 0 && curve[1] == 1000 && curve[^1] == 300 && curve[^2] == 100;
        Console.WriteLine($"  -> the boss's health curve, at most one point per half second: {health}");
        // turned-down inputs: only the player's own, inside the window, in tenths of a second
        var turned = new List<RejectRecord> { new(start.AddSeconds(2.5), you, 11000100, 0x13), new(start.AddSeconds(2.6), wendy, 17000010, 0x13), new(start.AddSeconds(500), you, 11000100, 0x13) };
        TimelineUpload? withRejects = TimelineBuilder.For(withTime with { Rejected = turned }, you, start, end, id => names.GetValueOrDefault(id));
        bool rejected = withRejects?.Rejected is { Count: 1 } r && r[0] == 25 && line?.Rejected is null;
        Console.WriteLine($"  -> the server's turned-down inputs of the player go along, nobody else's: {rejected}");
        // the focus: boss selected 0-4 s, an add 4-7 s, nothing 7-8, the boss again 8-10 (the target at the start comes from a change before the window)
        var targets = new List<TargetChange> { new(start.AddSeconds(-5), you, boss), new(start.AddSeconds(4), you, 777), new(start.AddSeconds(7), you, 0), new(start.AddSeconds(8), you, boss), new(start.AddSeconds(5), wendy, 777) };
        FocusUpload? focus = TimelineBuilder.FocusOf(withTime with { Targets = targets }, you, boss, start, start.AddSeconds(10));
        bool focused = focus is { OnBoss: 60, OnOthers: 30, Switches: 2 } && TimelineBuilder.FocusOf(withTime, you, boss, start, end) is null;
        Console.WriteLine($"  -> the focus counts the time on the boss and on anything else and the switches between targets: {focused}");
        // the target the game says an entity has selected holds until he changes it (no age limit); without it the last skill's aim counts for a while
        var chosen = new Aion2.Aion2CombatState();
        chosen.NoteAim(boss, you, start);
        chosen.NoteTarget(boss, wendy, start.AddSeconds(1));
        bool holds = chosen.TargetOf(boss, start.AddMinutes(5), TimeSpan.FromSeconds(5)) == wendy && chosen.TargetOf(you, start, TimeSpan.FromSeconds(5)) is null;
        chosen.NoteTarget(boss, 0, start.AddMinutes(6));
        bool dropped = chosen.TargetOf(boss, start.AddMinutes(7), TimeSpan.FromSeconds(5)) is null;
        Console.WriteLine($"  -> an entity's selected target holds until he drops it: {holds && dropped}");
        bool noUses = TimelineBuilder.For(FightExtras.Empty, you, start, end, _ => null) is null;
        Console.WriteLine($"  -> a player without skill uses has no time axis: {noUses}");
        return uptime && givers && noPotion && monster && spans && joined && capped && bossEffects && axis && health && rejected && focused && holds && dropped && noUses;
    }
}
