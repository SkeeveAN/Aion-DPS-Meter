using System.IO;
using System.Text.Json;
using AionDPS.Aion2;
using AionDPS.Aion2.Capture;

namespace AionDPS.Combat;

/// <summary>The stat calculator: stats from a stat frame, a change that must raise or lower the damage the way the stat says, the levers, and the character it cannot do (no physical attack).</summary>
public static class SelfCheckStatCalc
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Stat calculator:");
        // a frame as the game sends it (ids as in StatCalc.Spec, percentages in hundredths)
        var frame = new Dictionary<int, int>
        {
            [19] = 277, [317] = 1050, [31] = 542, [33] = 431, [425] = 337, [32] = 1000, [28] = 500, [379] = 1500,
            [44] = 600, [442] = 1750, [443] = 620, [589] = 560, [102] = 93, [56] = 259, [50] = 55,
        };
        Dictionary<StatKey, double> stats = StatCalc.FromFrame(frame);
        bool read = stats[StatKey.GearAttack] == 1050 && Math.Abs(stats[StatKey.AttackRatio] - 3.37) < 1e-9 && Math.Abs(stats[StatKey.Smite] - 6.2) < 1e-9 && stats[StatKey.BossAmp] == 0;
        Console.WriteLine($"  -> flat stats stay, percentages are read from hundredths, a stat the frame lacks is 0: {read}");

        var setup = new StatCalcSetup(CritRate: 30, Side: AttackSide.Back, SideRate: 60);
        StatCalcResult none = StatCalc.Calculate(stats, new Dictionary<StatKey, double>(), setup);
        bool zero = none.GainPercent == 0 && none.Before > 0 && none.Before == none.After;
        Console.WriteLine($"  -> no change, no gain: {zero}");

        double Gain(StatKey key, double delta) => StatCalc.Calculate(stats, new Dictionary<StatKey, double> { [key] = delta }, setup).GainPercent;
        bool signs = Gain(StatKey.GearAttack, 100) > 0 && Gain(StatKey.GearAttack, -100) < 0 && Gain(StatKey.AllAmp, 5) > 0 && Gain(StatKey.CritAmp, 10) > 0
            && Gain(StatKey.BackAmp, 10) > 0 && Gain(StatKey.FrontAmp, 10) == 0 && Gain(StatKey.Smite, 5) > 0 && Gain(StatKey.AttackRatio, 5) > 0;
        Console.WriteLine($"  -> more of a stat gives more damage, less gives less; the front stat does nothing while the player hits from behind: {signs}");

        // the side counts only when the player hits from there; "unknown" ignores both
        var unknown = setup with { Side = AttackSide.Unknown };
        bool side = StatCalc.Calculate(stats, new Dictionary<StatKey, double> { [StatKey.BackAmp] = 50 }, unknown).GainPercent == 0;
        // a crit amplification matters more the more crits there are
        double lowCrit = StatCalc.Calculate(stats, new Dictionary<StatKey, double> { [StatKey.CritAmp] = 20 }, setup with { CritRate = 10 }).GainPercent;
        double highCrit = StatCalc.Calculate(stats, new Dictionary<StatKey, double> { [StatKey.CritAmp] = 20 }, setup with { CritRate = 60 }).GainPercent;
        bool crits = highCrit > lowCrit && lowCrit > 0;
        Console.WriteLine($"  -> unknown side adds nothing, a crit amplification is worth more with more crits ({lowCrit:0.00} % at 10, {highCrit:0.00} % at 60): {side && crits}");

        // the boss' resistance cuts the amplifications, the expert buffs add to them
        double resisted = StatCalc.Calculate(stats, new Dictionary<StatKey, double> { [StatKey.AllAmp] = 5 }, setup with { BossResistance = 20 }).GainPercent;
        double plain = Gain(StatKey.AllAmp, 5);
        bool expert = Math.Abs(resisted - plain) > 1e-6 && StatCalc.Index(stats, setup with { AmpBuff = 10 }) > StatCalc.Index(stats, setup);
        Console.WriteLine($"  -> expert setting: the same amplification is worth more against a boss that resists less, a buff raises the index: {expert}");

        IReadOnlyList<StatCalcLever> levers = StatCalc.Levers(stats, setup);
        StatCalcLever? attack = levers.FirstOrDefault(l => l.Key == StatKey.GearAttack);
        bool lever = attack is not null && Math.Abs(Gain(StatKey.GearAttack, attack.ForOnePercent) - 1) < 0.05 && levers.All(l => l.ForOnePercent > 0) && levers.All(l => l.Key != StatKey.FrontAmp);
        Console.WriteLine($"  -> the amount a lever names really gives one percent, a stat that does nothing is not listed: {lever}");

        bool supported = StatCalc.IsSupported(frame) && !StatCalc.IsSupported(new Dictionary<int, int> { [19] = 100, [1] = 5 });
        Console.WriteLine($"  -> a character without a physical attack is not calculated: {supported}");
        bool recording = RunRecording(frame);
        bool network = RunNetworkReport();
        return read && zero && signs && side && crits && expert && lever && supported && recording && network;
    }

    /// <summary>The network report: the shape the server asks for, a file name with the version and the start of the period, and nothing of the player's machine in it (no IP address, no PC or account name).</summary>
    private static bool RunNetworkReport()
    {
        string anon = "0123456789abcdef0123456789abcdef";
        string json = NetworkReport.Build(DateTime.Now, null, anon);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        bool shape = root.GetProperty("format").GetString() == "aiondps-network-report" && root.GetProperty("formatVersion").GetInt32() == 1 && root.GetProperty("anonymousId").GetString() == anon
            && root.GetProperty("clientVersion").GetString()!.Length > 0 && root.GetProperty("periodSeconds").GetInt32() >= 0 && root.GetProperty("stalls").ValueKind == JsonValueKind.Array
            && root.GetProperty("timeline").GetProperty("rows").ValueKind == JsonValueKind.Array && root.GetProperty("adapter").GetProperty("selection").GetString() == "automatic";
        bool noIp = !System.Text.RegularExpressions.Regex.IsMatch(json, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
        bool noNames = !json.Contains(Environment.MachineName, StringComparison.OrdinalIgnoreCase) && !json.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase);
        string file = NetworkReport.FileName(new DateTime(2026, 10, 19, 14, 32, 5));
        bool name = file.StartsWith("aiondps_", StringComparison.Ordinal) && file.EndsWith("_2026-10-19_14-32-05.json", StringComparison.Ordinal) && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        bool withoutId = JsonDocument.Parse(NetworkReport.Build(DateTime.Now, null, null)).RootElement.TryGetProperty("anonymousId", out _) == false;
        Console.WriteLine($"  -> the network report has the shape the server wants, a file name aiondps_VERSION_DATE_TIME.json, no IP address and no PC or account name (the file for the player's own use has no identifier): {shape && noIp && noNames && name && withoutId}");
        return shape && noIp && noNames && name && withoutId;
    }

    /// <summary>The recording: stat changes are written down, it stops by itself after ten minutes, and the document holds the hits of monsters but no name and no player.</summary>
    private static bool RunRecording(Dictionary<int, int> frame)
    {
        DateTime t0 = new(2026, 10, 19, 18, 0, 0);
        var recorder = new StatRecorder();
        recorder.Start(t0, frame);
        var buffed = new Dictionary<int, int>(frame) { [28] = 800, [500] = 5 };
        bool quiet = !recorder.Poll(t0.AddSeconds(5), frame) && recorder.Changes.Count == 0;
        recorder.Poll(t0.AddSeconds(12), buffed);
        bool changes = recorder.Changes.Count == 2 && recorder.Changes.Any(c => c.Id == 28 && c.Value == 800 && Math.Abs(c.At - 12) < 1e-6) && recorder.Changes.Any(c => c.Id == 500);
        bool stops = !recorder.Poll(t0.AddMinutes(9), buffed) && recorder.Poll(t0.AddMinutes(10), buffed);
        IReadOnlyDictionary<int, int> end = recorder.Stop();
        Console.WriteLine($"  -> a recording writes down the stats that change, stops by itself after ten minutes: {quiet && changes && stops && !recorder.Running && end[28] == 800}");

        const int you = 1, monster = 100, player = 7;
        var hits = new List<DamageEvent>
        {
            new(t0.AddSeconds(10), you, monster, 1000, false, "Strike", true, SkillId: 11, Kinds: HitKinds.Back | HitKinds.Perfect),
            new(t0.AddSeconds(11), you, monster, 500, false, "Strike", false, SkillId: 11, Kinds: HitKinds.Front),
            new(t0.AddSeconds(12), you, monster, 90, false, "Burn", false, IsTick: true, SkillId: 12),
            new(t0.AddSeconds(13), you, monster, 2000, false, "Slam", false, SkillId: 13, Kinds: HitKinds.Double, HitCount: 2),
            new(t0.AddSeconds(14), you, player, 700, false, "Strike", false, SkillId: 11, Kinds: HitKinds.None),
            new(t0.AddSeconds(15), you, monster, 300, true, "Heal", false, SkillId: 14),
            new(t0.AddSeconds(16), you, monster, 123, false, "Strike", false, SkillId: 11, Estimated: true),
        };
        var extras = new FightExtras(
            new[] { new SkillUse(t0.AddSeconds(10), you, 11), new SkillUse(t0.AddSeconds(10), 9, 99) },
            new[] { new BuffEvent(t0.AddSeconds(2), BuffKind.On, you, 1, 5000, 9, 30_000), new BuffEvent(t0.AddSeconds(3), BuffKind.On, 9, 2, 5001, 9, 30_000) },
            Array.Empty<StaggerEvent>(), new Dictionary<int, string>());
        var setup = new StatRecordingSetup("expert", "back", 60, 30, 0, 10, 0, 0, 20, 150, 100);
        string json = StatRecording.Build("0123456789abcdef0123456789abcdef", t0, t0.AddMinutes(4), setup, new StatRecordingCharacter(1, 45, 1304, StatRecording.FactionOf(1304)),
            frame, buffed, recorder.Changes, hits, extras, you, id => id == monster ? 2300104 : null, id => id == monster ? "Harcon" : null);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        JsonElement totals = root.GetProperty("hits");
        bool shape = root.GetProperty("kind").GetString() == "stat-recording" && root.GetProperty("character").GetProperty("faction").GetString() == "Elyos"
            && root.GetProperty("anonymousId").GetString()!.Length == 32 && root.GetProperty("statsEnd").GetProperty("28").GetInt32() == 800 && root.GetProperty("durationSeconds").GetInt32() == 240;
        // 5 direct hits on monster and player (the heal and the estimated one are not hits), one tick, one crit, one back, one front, one perfect, one double, one multi
        bool counts = totals.GetProperty("hits").GetInt32() == 4 && totals.GetProperty("ticks").GetInt32() == 1 && totals.GetProperty("crits").GetInt32() == 1 && totals.GetProperty("backs").GetInt32() == 1
            && totals.GetProperty("fronts").GetInt32() == 1 && totals.GetProperty("perfects").GetInt32() == 1 && totals.GetProperty("doubles").GetInt32() == 1 && totals.GetProperty("multis").GetInt32() == 1;
        JsonElement targets = root.GetProperty("targets");
        bool monstersOnly = targets.GetArrayLength() == 1 && targets[0].GetProperty("npcId").GetInt32() == 2300104 && targets[0].GetProperty("name").GetString() == "Harcon";
        bool own = root.GetProperty("effects").GetArrayLength() == 1 && root.GetProperty("effects")[0].GetProperty("own").GetBoolean() == false && root.GetProperty("uses").GetArrayLength() == 1;
        bool noPersonal = !json.Contains("characterName", StringComparison.OrdinalIgnoreCase) && !json.Contains("guild", StringComparison.OrdinalIgnoreCase) && !json.Contains("\"name\": \"Aahz\"");
        Console.WriteLine($"  -> the document holds the figures, only monsters as targets, only own skills and the effects on the player, and no name: {shape && counts && monstersOnly && own && noPersonal}");
        return quiet && changes && stops && end[28] == 800 && shape && counts && monstersOnly && own && noPersonal;
    }
}
