using System.IO;
using AionSniffer.ChatLog;
using AionSniffer.Data;
using AionSniffer.Protocol;

namespace AionSniffer.Combat;

/// <summary>
/// Verifies the DPS/iDPS math, the SM_ATTACK multi-hit parser, the skill database, and the
/// live-aggregation wiring against synthetic and real reference data, all without needing a live
/// capture -- run via `dotnet run -- selftest`. Includes the Gladiator/Zauberer "ALL view"
/// thought experiment that motivated distinguishing DPS from iDPS in the first place, and the
/// real numbers pulled from a public myaion.eu boss-fight session (see README) to ground-truth
/// the iDPS formula.
/// </summary>
public static class SelfCheck
{
    private const int GladiatorId = 1;
    private const int ZaubererId = 2;
    private const int BossId = 100;

    public static bool Run()
    {
        bool ok = true;
        ok &= RunGladiatorVsZaubererScenario();
        ok &= RunMyAionReplayScenario();
        ok &= RunAttackPacketParsingScenario();
        ok &= RunSkillDatabaseScenario();
        ok &= RunLiveAggregatorScenario();
        ok &= RunChatLogParserScenario();
        ok &= RunChatLogRealWorldPatternsScenario();
        return ok;
    }

    /// <summary>
    /// Gladiator hits a training dummy every second for 300s (constant weaving); Zauberer hits
    /// theirs only every 60s with a big nuke. Demonstrates why "DPS" (ALL view, wall-clock) alone
    /// is misleading, and what "active-only" tries (and, for a lone isolated hit, fails) to fix.
    /// </summary>
    private static bool RunGladiatorVsZaubererScenario()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<DamageEvent>();

        for (int i = 0; i < 300; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i), GladiatorId, BossId, 1000, false));
        }

        for (int i = 0; i < 5; i++)
        {
            events.Add(new DamageEvent(start.AddSeconds(i * 60), ZaubererId, BossId, 50_000, false));
        }

        double? gladWall = DpsCalculator.AllDpsWallClock(events, GladiatorId);
        double? zaubWall = DpsCalculator.AllDpsWallClock(events, ZaubererId);
        double? gladActive = DpsCalculator.AllDpsActiveOnly(events, GladiatorId, TimeSpan.FromSeconds(3));
        double? zaubActive = DpsCalculator.AllDpsActiveOnly(events, ZaubererId, TimeSpan.FromSeconds(3));

        Console.WriteLine("[selftest] Gladiator/Zauberer ALL-view scenario:");
        Console.WriteLine($"  Gladiator: wallclock={Fmt(gladWall)} active={Fmt(gladActive)} (300 hits x 1000 dmg, 1s apart)");
        Console.WriteLine($"  Zauberer:  wallclock={Fmt(zaubWall)} active={Fmt(zaubActive)} (5 hits x 50000 dmg, 60s apart)");

        // Expectation: both spans have more than one hit, so wall-clock is well-defined (not
        // null) for both here -- assert that explicitly so a regression fails loudly instead of
        // a lifted "null > null is false" comparison quietly passing or failing for the wrong
        // reason. Then: on a raw wall-clock "ALL" reading the bursty Zauberer is NOT obviously
        // behind the sustained Gladiator (~1042 vs ~1003) -- this is the exact unfairness the
        // user's original example was about.
        bool wallClockLooksUnfair = gladWall is double gw && zaubWall is double zw && zw > gw * 0.9;

        // Expectation: with a 3s idle threshold, every one of the Gladiator's 1s gaps counts
        // (constant activity), so a real rate comes out. Every one of the Zauberer's 60s gaps
        // is excluded outright -- there's no gap short enough to count, so there is no active
        // time to divide by. AllDpsActiveOnly returns null for that rather than the raw damage
        // total (an earlier version did that, and it reads as a UI bug -- "250,000 iDPS" with no
        // context looks broken, not "undefined"; a real build would show "n/a" here instead).
        bool gladiatorActiveIsReasonable = gladActive is double g && g > 900 && g < 1100;
        bool zaubererActiveIsUndefined = zaubActive is null;

        Console.WriteLine($"  -> wall-clock makes the bursty caster look competitive: {wallClockLooksUnfair}");
        Console.WriteLine($"  -> active-only correctly rates the sustained gladiator: {gladiatorActiveIsReasonable}");
        Console.WriteLine($"  -> active-only correctly reports \"undefined\" for the isolated-burst caster: {zaubererActiveIsUndefined}");

        return wallClockLooksUnfair && gladiatorActiveIsReasonable && zaubererActiveIsUndefined;
    }

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("F1") : "n/a";

    /// <summary>
    /// Replays the real numbers from myaion.eu's public session /PvESession/1716393: player
    /// "Strohmie" dealt 84,360,277 damage to one boss at a displayed rate of 438,288. Feeding
    /// synthetic hits spanning the implied ~192.48s duration should reproduce that rate via
    /// <see cref="DpsCalculator.TargetIDps"/>.
    /// </summary>
    private static bool RunMyAionReplayScenario()
    {
        const long totalDamage = 84_360_277;
        const int expectedIDps = 438_288;
        double impliedDurationSeconds = (double)totalDamage / expectedIDps; // ~192.48s

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const int hitCount = 30;
        long perHit = totalDamage / hitCount;
        long remainder = totalDamage - perHit * hitCount;

        var events = new List<DamageEvent>();
        for (int i = 0; i < hitCount; i++)
        {
            double t = i * impliedDurationSeconds / (hitCount - 1);
            long amount = perHit + (i == hitCount - 1 ? remainder : 0);
            events.Add(new DamageEvent(start.AddSeconds(t), GladiatorId, BossId, amount, false));
        }

        double? iDps = DpsCalculator.TargetIDps(events, BossId, GladiatorId);
        Console.WriteLine("[selftest] myaion.eu replay (Strohmie vs. boss, /PvESession/1716393):");
        Console.WriteLine($"  expected iDPS={expectedIDps}, computed iDPS={Fmt(iDps)}");

        bool matches = iDps is double v && Math.Abs(v - expectedIDps) < 1.0;
        Console.WriteLine($"  -> matches within rounding: {matches}");
        return matches;
    }

    /// <summary>
    /// Builds a synthetic SM_ATTACK body byte-for-byte the way the emulator's SM_ATTACK.java
    /// writeImpl would (see CombatPacketParser.TryParseAttack's doc comment), covering all three
    /// extra-field sizes the shieldType switch can produce: 0 bytes (plain hit), 12 bytes
    /// (protected hit), and 28 bytes (the shieldType-16 / catch-all case) -- followed by one more
    /// plain hit, so a wrong 28-byte guess would misalign it and get caught, not just silently
    /// produce a plausible-looking-but-wrong result. An earlier version only covered the 0- and
    /// 12-byte branches, leaving the 28-byte catch-all -- the one most likely to hide a bug --
    /// completely unverified.
    /// </summary>
    private static bool RunAttackPacketParsingScenario()
    {
        const int attacker = 0x1000_1234;
        const int target = 0x2000_5678;
        const byte targetHp = 80;
        const byte attackerHp = 95;

        var body = new List<byte>();
        WriteI32(body, attacker);
        body.Add(1); // attackNo
        WriteU16(body, 0); // time
        body.Add(0); // simpleAttackType
        body.Add(0); // type
        WriteI32(body, target);
        body.Add(targetHp);
        body.Add(attackerHp);
        WriteI32(body, 0); // counter flag
        body.Add(4); // hitCount

        // Hit 1: plain, shieldType 0 -> no extra fields.
        WriteI32(body, 1000);
        body.Add(5); // attackStatusId
        body.Add(0); // shieldType
        body.AddRange(new byte[16]);

        // Hit 2: protected, shieldType 8 -> +12 bytes (protectorId, protectedDamage, protectedSkillId).
        WriteI32(body, 2000);
        body.Add(3); // attackStatusId
        body.Add(8); // shieldType
        body.AddRange(new byte[16]);
        WriteI32(body, 999); // protectorId
        WriteI32(body, 111); // protectedDamage
        WriteI32(body, 222); // protectedSkillId

        // Hit 3: reflected, shieldType 16 -> +28 bytes (7 x i32, values irrelevant here).
        WriteI32(body, 3000);
        body.Add(7); // attackStatusId
        body.Add(16); // shieldType
        body.AddRange(new byte[16]);
        for (int i = 0; i < 7; i++)
        {
            WriteI32(body, 0);
        }

        // Hit 4: plain again. Only reachable at the right values if hit 3's 28-byte skip was
        // correct -- a wrong extraLen for shieldType 16 would misalign this hit's fields instead.
        WriteI32(body, 4000);
        body.Add(9); // attackStatusId
        body.Add(0); // shieldType
        body.AddRange(new byte[16]);

        body.Add(0); // trailing list-size byte

        var parsed = CombatPacketParser.TryParseAttack(body.ToArray());

        Console.WriteLine("[selftest] SM_ATTACK multi-hit parsing (synthetic, plain + 12-byte + 28-byte + plain hits):");
        Console.WriteLine($"  parsed: {(parsed is null ? "null" : CombatPacketParser.TryDescribeAttack(body.ToArray()))}");

        bool structureOk = parsed is not null
            && parsed.Hits.Count == 4
            && parsed.AttackerObjectId == attacker
            && parsed.TargetObjectId == target
            && parsed.TargetHpPercent == targetHp
            && parsed.AttackerHpPercent == attackerHp;
        bool hit1Ok = parsed is not null && parsed.Hits[0] == new CombatPacketParser.AttackHit(1000, 5, 0);
        bool hit2Ok = parsed is not null && parsed.Hits[1] == new CombatPacketParser.AttackHit(2000, 3, 8);
        bool hit3Ok = parsed is not null && parsed.Hits[2] == new CombatPacketParser.AttackHit(3000, 7, 16);
        bool hit4Ok = parsed is not null && parsed.Hits[3] == new CombatPacketParser.AttackHit(4000, 9, 0);

        Console.WriteLine($"  -> header fields correct: {structureOk}");
        Console.WriteLine($"  -> hit 1 (plain, 0 extra bytes) correct: {hit1Ok}");
        Console.WriteLine($"  -> hit 2 (shielded, 12 extra bytes) correct: {hit2Ok}");
        Console.WriteLine($"  -> hit 3 (reflected, 28 extra bytes -- previously untested) correct: {hit3Ok}");
        Console.WriteLine($"  -> hit 4 (plain, only aligned right if hit 3's 28-byte skip was correct): {hit4Ok}");

        return structureOk && hit1Ok && hit2Ok && hit3Ok && hit4Ok;
    }

    private static void WriteI32(List<byte> buf, int value) => buf.AddRange(BitConverter.GetBytes(value));
    private static void WriteU16(List<byte> buf, ushort value) => buf.AddRange(BitConverter.GetBytes(value));

    /// <summary>
    /// Verifies SkillDatabase actually finds and parses assets/skills/skills_en_4x.json at
    /// runtime -- this exercises the real deployment path (AppContext.BaseDirectory + the
    /// csproj's CopyToOutputDirectory setting for assets/), not just the JSON parsing in
    /// isolation. Skill 1 ("Basic Sword Training") is the first row in that file.
    /// </summary>
    private static bool RunSkillDatabaseScenario()
    {
        var table = SkillDatabase.Load();
        string knownName = SkillDatabase.DisplayName(1);
        string unknownName = SkillDatabase.DisplayName(999_999_999);

        Console.WriteLine("[selftest] SkillDatabase load (assets/skills/skills_en_4x.json):");
        Console.WriteLine($"  loaded {table.Count} skills; skill 1 = \"{knownName}\"; unknown id -> \"{unknownName}\"");

        bool loadedSomething = table.Count > 500; // expect ~974; loose bound so minor rescrapes don't break this
        bool knownResolved = knownName == "Basic Sword Training";
        bool unknownFallsBack = unknownName.Contains("unknown");

        Console.WriteLine($"  -> table loaded with a plausible size: {loadedSomething}");
        Console.WriteLine($"  -> skill 1 resolved to its real name: {knownResolved}");
        Console.WriteLine($"  -> unknown id falls back cleanly instead of throwing: {unknownFallsBack}");

        if (!loadedSomething || !knownResolved)
        {
            Console.WriteLine($"  (looked for assets at: {Path.Combine(AppContext.BaseDirectory, "assets", "skills", "skills_en_4x.json")})");
        }

        return loadedSomething && knownResolved && unknownFallsBack;
    }

    /// <summary>
    /// Verifies the AionSession -> LiveAggregator wiring end to end at the object level (two
    /// synthetic AttackPacket results fed in exactly the way Program.cs's HandleDecoded would),
    /// without needing bytes or a capture: two attackers hitting the same boss, checks the
    /// per-source totals AND the rendered DPS column -- specifically that a source with only one
    /// attributed hit (the normal state of the first line of output on every real run) shows
    /// "n/a" rather than its damage total dressed up as a rate.
    /// </summary>
    private static bool RunLiveAggregatorScenario()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var aggregator = new LiveAggregator();

        var gladiatorHits = new List<CombatPacketParser.AttackHit>
        {
            new(1000, 5, 0),
            new(1200, 5, 0),
        };
        var zaubererHits = new List<CombatPacketParser.AttackHit> { new(50_000, 7, 0) };

        aggregator.IngestAttack(start, new CombatPacketParser.AttackPacket(GladiatorId, BossId, 90, 100, gladiatorHits));
        aggregator.IngestAttack(start.AddSeconds(2), new CombatPacketParser.AttackPacket(GladiatorId, BossId, 80, 100, new List<CombatPacketParser.AttackHit> { new(1100, 5, 0) }));
        aggregator.IngestAttack(start.AddSeconds(1), new CombatPacketParser.AttackPacket(ZaubererId, BossId, 85, 95, zaubererHits));

        Console.WriteLine("[selftest] LiveAggregator wiring (synthetic AttackPacket objects, as HandleDecoded would feed them):");
        Console.WriteLine($"  {aggregator.Summarize()}");

        int gladiatorEvents = aggregator.Events.Count(e => e.SourceObjectId == GladiatorId);
        long gladiatorTotal = aggregator.Events.Where(e => e.SourceObjectId == GladiatorId).Sum(e => e.Amount);
        long zaubererTotal = aggregator.Events.Where(e => e.SourceObjectId == ZaubererId).Sum(e => e.Amount);

        bool gladiatorEventCountOk = gladiatorEvents == 3; // 2 hits in the first packet + 1 in the second
        bool gladiatorTotalOk = gladiatorTotal == 1000 + 1200 + 1100;
        bool zaubererTotalOk = zaubererTotal == 50_000;

        // Caught by review: zauberer has exactly one attributed hit here, which is the normal
        // state of the very first line of live output on every real capture run, not an edge
        // case. Summarize() must show "n/a" for a DPS rate that has no time span to be computed
        // from -- not the raw 50,000 damage total masquerading as "50000 DPS", which is the exact
        // bug already fixed once for AllDpsActiveOnly and had quietly regressed via
        // AllDpsWallClock. Assert on the actual rendered string, not just the underlying totals:
        // that's what let the bug hide behind a passing test the first time.
        string summary = aggregator.Summarize();
        bool zaubererShowsNotAvailable = summary.Contains("0x00000002: 50000 dmg (n/a DPS)");
        bool zaubererDoesNotShowTotalAsRate = !summary.Contains("(50000 DPS)");
        double? gladiatorWallDps = DpsCalculator.AllDpsWallClock(aggregator.Events, GladiatorId);
        bool gladiatorDpsIsReal = gladiatorWallDps is double d && d > 0;

        Console.WriteLine($"  -> gladiator's 3 hits across 2 packets all recorded: {gladiatorEventCountOk}");
        Console.WriteLine($"  -> gladiator total damage correct: {gladiatorTotalOk}");
        Console.WriteLine($"  -> zauberer total damage correct: {zaubererTotalOk}");
        Console.WriteLine($"  -> zauberer's single hit renders as \"n/a\" DPS, not a fake rate: {zaubererShowsNotAvailable && zaubererDoesNotShowTotalAsRate}");
        Console.WriteLine($"  -> gladiator (multiple hits, real time span) still gets a real DPS number: {gladiatorDpsIsReal}");

        return gladiatorEventCountOk && gladiatorTotalOk && zaubererTotalOk
            && zaubererShowsNotAvailable && zaubererDoesNotShowTotalAsRate && gladiatorDpsIsReal;
    }

    /// <summary>
    /// Verifies ChatLogParser against real lines taken verbatim from an OriginAion Chat.log
    /// (2026-08-23), plus a synthetic duplicate-with-interleaving block modeled on a pattern
    /// found in that same real file: two clients sharing one Chat.log each logged an identical
    /// broadcast line, but a combat line from the OTHER client landed physically between the two
    /// copies (see ChatLogParser remarks) -- so the dedup check below only counts as verified if
    /// the interleaving still gets caught, not just an adjacent-duplicate case.
    /// </summary>
    private static bool RunChatLogParserScenario()
    {
        var lines = new[]
        {
            "2026.08.23 21:31:08 : Ulgorn Raider inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:31:09 : Ulgorn Raider inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:31:10 : Training Dummy evaded Ulgorn Raider's attack. ",
            "2026.08.23 21:32:07 : You inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:32:17 : You are too far from the target to use that skill. ",
            "2026.08.23 21:32:04 : Sniggy has logged in. ",
            "2026.08.23 21:31:08 : [3.LFG] [charname:Shera;1.0000 0.6941 0.6941]: [cmd:Shera;ZauPvKJjMpNiscgHo48bYhTuRUkaUzjxBls+lJtRs9s=]HOLD GROUP ",
            // Duplicate-with-interleaving, modeled on real lines 96/97/98 of that capture.
            "2026.08.23 21:32:16 : Barracuda inflicted 500 damage on Training Dummy. ",
            "2026.08.23 21:32:16 : Ulgorn Raider inflicted 1 damage on Training Dummy. ",
            "2026.08.23 21:32:16 : Barracuda inflicted 500 damage on Training Dummy. ",
        };

        var parser = new ChatLogParser();
        var events = parser.Parse(lines);

        Console.WriteLine("[selftest] ChatLogParser (real OriginAion Chat.log lines + synthetic dedup case):");
        Console.WriteLine($"  parsed {events.Count} damage events from {lines.Length} lines");

        // 3 Ulgorn Raider hits (the interleaved 21:32:16 one is a real, distinct event -- different
        // timestamp bucket than the first two, and a different message than Barracuda's within its
        // own bucket, so seenThisBucket correctly leaves it alone) + 1 "You" hit + 1 of the 2
        // duplicate Barracuda hits. Caught by review: an earlier version of this assertion (== 4)
        // undercounted by excluding exactly the event the dedup test was designed to prove isn't
        // wrongly dropped -- it passed only because a test bug and a hypothetical dedup-over-reach
        // bug would have looked the same from the total alone; the per-source checks below (Ulgorn
        // count, Barracuda total) are what actually distinguish them.
        bool countOk = events.Count == 5;
        int ulgornHitCount = events.Count(e => parser.Names.NameFor(e.SourceObjectId) == "Ulgorn Raider");
        bool ulgornCountOk = ulgornHitCount == 3;
        bool evadeSkipped = !events.Any(e => parser.Names.NameFor(e.SourceObjectId) == "Training Dummy");
        bool youResolved = events.Any(e => parser.Names.NameFor(e.SourceObjectId) == "You" && e.Amount == 1);
        int ulgornId = parser.Names.GetOrAssignId("Ulgorn Raider");
        int youId = parser.Names.GetOrAssignId("You");
        bool stableDistinctIds = ulgornId != youId && ulgornId == parser.Names.GetOrAssignId("Ulgorn Raider");
        long barracudaTotal = events
            .Where(e => parser.Names.NameFor(e.SourceObjectId) == "Barracuda")
            .Sum(e => e.Amount);
        bool duplicateInterleavedAcrossOtherLineWasDropped = barracudaTotal == 500; // not 1000

        Console.WriteLine($"  -> event count correct (dedup caught the interleaved duplicate): {countOk}");
        Console.WriteLine($"  -> non-damage lines (evade/too-far/login/LFG) produced no events: {evadeSkipped}");
        Console.WriteLine($"  -> \"You\" resolved to a real damage event: {youResolved}");
        Console.WriteLine($"  -> name registry gives stable, distinct ids: {stableDistinctIds}");
        Console.WriteLine($"  -> the interleaved Ulgorn Raider hit still counted (all 3 present, not just 2): {ulgornCountOk}");
        Console.WriteLine($"  -> duplicate broadcast-style damage line (split by an unrelated line) counted once: {duplicateInterleavedAcrossOtherLineWasDropped}");

        return countOk && ulgornCountOk && evadeSkipped && youResolved && stableDistinctIds && duplicateInterleavedAcrossOtherLineWasDropped;
    }

    /// <summary>
    /// Verifies ChatLogParser against real lines taken verbatim from a ~44k-line OriginAion play
    /// session (2026-08-24) -- a much richer sample than RunChatLogParserScenario's small,
    /// hand-picked one. This is what surfaced the "." thousands-separator number format, every
    /// heal phrasing, incoming damage, and the reflected-damage-to-self line whose naive parse
    /// split the local player's identity into "You" and "you" (see ChatLogParser's remarks) --
    /// found by running the regexes against the full file and cross-checking the resulting
    /// distinct-name list, not by reasoning about any single line in isolation. This test locks
    /// in exactly that finding so it can't silently regress.
    /// </summary>
    private static bool RunChatLogRealWorldPatternsScenario()
    {
        var lines = new[]
        {
            "2026.08.24 22:08:37 : Critical Hit!You inflicted 1.911 damage on Icy Kalgolem by using Force Blast I. ",
            "2026.08.24 22:08:38 : Critical Hit! You inflicted 1.022 critical damage on Icy Kalgolem. ",
            "2026.08.24 22:08:39 : Icy Kalgolem has inflicted 994 damage on you by using Power Attack. ",
            "2026.08.24 22:08:40 : You received 172 damage from Icy Kalgolem. ",
            // Found by terminal_windows against a SECOND, larger real Chat.log after this test's
            // first version shipped without these two lines: the "Critical Hit!" prefix is real on
            // both incoming-damage patterns too, not just the outgoing one covered above. Missed
            // initially because the first validation sample happened not to include an incoming
            // crit -- exactly the kind of gap only a bigger real sample exposes.
            "2026.08.24 22:08:39 : Critical Hit!Sparky has inflicted 999 damage on you by using Wing Buffet. ",
            "2026.08.24 22:08:40 : Critical Hit!You received 888 damage from Sparky. ",
            "2026.08.24 22:08:41 : Your attack on Torch Spirit Iprita was reflected and inflicted 246 damage on you. ",
            "2026.08.24 22:08:42 : You restored 95 of Hestika's HP by using Major Recovery Potion. ",
            "2026.08.24 22:08:43 : Mortelle recovered 156 HP by using Healing Light I. ",
            "2026.08.24 22:08:44 : Thai recovered 195 HP because Inss used Healing Light I. ",
            "2026.08.24 22:08:45 : You recovered 157 HP because Gsghost used Healing Light VI on you. ",
            // Deliberately unattributed DoT-tick lines (see ChatLogParser's "Known, deliberate
            // gaps") -- must NOT produce events, since neither names a real source character.
            "2026.08.24 22:08:46 : Drakan Crewhand received 2.649 damage due to the effect of Spray Drana Acid. ",
            "2026.08.24 22:08:47 : You receive 56 damage due to Fire Strike. ",
        };

        var parser = new ChatLogParser();
        var events = parser.Parse(lines);
        string? NameOf(int id) => parser.Names.NameFor(id);

        // Found by terminal_windows against the same second, larger real session: LiveAggregator
        // (and, separately, MainWindow's row-building code) summed ALL events including heals into
        // the damage total, while the DPS *rate* next to it already filtered heals out via
        // DpsCalculator -- so a pure healer rendered as e.g. "Potion: 3526 dmg (n/a DPS)", a
        // damage number with no matching rate, because the total and the rate were silently
        // computed over two different event sets. Wiring a real LiveAggregator here, with this
        // heal-bearing event set, is what actually exercises that consumer-side bug -- the parser
        // itself was never at fault (IsHeal was always set correctly), so a parser-only test
        // couldn't have caught this.
        var aggregator = new LiveAggregator();
        aggregator.IngestEvents(events);
        string summary = aggregator.Summarize(NameOf);
        bool healersExcludedFromDamageSummary =
            !summary.Contains("Hestika") && !summary.Contains("Mortelle") &&
            !summary.Contains("Inss") && !summary.Contains("Gsghost") && !summary.Contains("Thai");

        Console.WriteLine("[selftest] ChatLogParser real-world patterns (crit/skill/incoming/reflect/heal variants):");
        Console.WriteLine($"  parsed {events.Count} events from {lines.Length} lines");

        bool critWithSkillOk = events.Any(e => !e.IsHeal && e.Amount == 1911
            && NameOf(e.SourceObjectId) == "You" && NameOf(e.TargetObjectId) == "Icy Kalgolem");
        bool critWordOk = events.Any(e => !e.IsHeal && e.Amount == 1022
            && NameOf(e.SourceObjectId) == "You" && NameOf(e.TargetObjectId) == "Icy Kalgolem");
        bool incomingSkillOk = events.Any(e => !e.IsHeal && e.Amount == 994
            && NameOf(e.SourceObjectId) == "Icy Kalgolem" && NameOf(e.TargetObjectId) == "You");
        bool incomingBasicOk = events.Any(e => !e.IsHeal && e.Amount == 172
            && NameOf(e.SourceObjectId) == "Icy Kalgolem" && NameOf(e.TargetObjectId) == "You");
        bool critIncomingSkillOk = events.Any(e => !e.IsHeal && e.Amount == 999
            && NameOf(e.SourceObjectId) == "Sparky" && NameOf(e.TargetObjectId) == "You");
        bool critIncomingBasicOk = events.Any(e => !e.IsHeal && e.Amount == 888
            && NameOf(e.SourceObjectId) == "Sparky" && NameOf(e.TargetObjectId) == "You");
        bool reflectOk = events.Any(e => !e.IsHeal && e.Amount == 246
            && NameOf(e.SourceObjectId) == "Torch Spirit Iprita" && NameOf(e.TargetObjectId) == "You");
        bool healOtherOk = events.Any(e => e.IsHeal && e.Amount == 95
            && NameOf(e.SourceObjectId) == "You" && NameOf(e.TargetObjectId) == "Hestika");
        bool healSelfOtherCharacterOk = events.Any(e => e.IsHeal && e.Amount == 156
            && NameOf(e.SourceObjectId) == "Mortelle" && NameOf(e.TargetObjectId) == "Mortelle");
        bool healByOtherThirdPersonOk = events.Any(e => e.IsHeal && e.Amount == 195
            && NameOf(e.SourceObjectId) == "Inss" && NameOf(e.TargetObjectId) == "Thai");
        bool healByOtherOnYouOk = events.Any(e => e.IsHeal && e.Amount == 157
            && NameOf(e.SourceObjectId) == "Gsghost" && NameOf(e.TargetObjectId) == "You");

        // The actual bug this test was written to lock in: every "You" reference above (attacker
        // in some events, target in others, via three differently-phrased "on you" patterns) must
        // resolve to the SAME id -- not a second "you"/"You" split from case differences in the raw
        // log text, which is exactly what happened before ReflectedDamagePattern existed.
        int youId = parser.Names.GetOrAssignId("You");
        bool noIdentitySplit = !events.Any(e => e.SourceObjectId != youId && NameOf(e.SourceObjectId) == "You")
            && !events.Any(e => e.TargetObjectId != youId && NameOf(e.TargetObjectId) == "You")
            && parser.Names.NameFor(youId) == "You";

        // Direct check for the exact failure mode terminal_windows found: an unstripped "Critical
        // Hit!" prefix leaking into a captured name (either side, either incoming pattern). Checked
        // separately from noIdentitySplit above because a regression here wouldn't necessarily
        // produce a string equal to "You" -- e.g. "Critical Hit!You" is a different string, so it
        // wouldn't trip that check, but it's exactly as wrong.
        bool noCriticalHitLeak = !events.Any(e =>
            (NameOf(e.SourceObjectId) ?? "").Contains("Critical Hit") || (NameOf(e.TargetObjectId) ?? "").Contains("Critical Hit"));

        bool dotLinesProducedNoEvents = events.Count == 11; // the 2 unattributed DoT lines above must not add events

        Console.WriteLine($"  -> crit + skill, grouped number 1.911 -> 1911: {critWithSkillOk}");
        Console.WriteLine($"  -> crit, \"critical damage\" wording, grouped number 1.022 -> 1022: {critWordOk}");
        Console.WriteLine($"  -> incoming skill damage (has inflicted...on you): {incomingSkillOk}");
        Console.WriteLine($"  -> incoming basic damage (received...from): {incomingBasicOk}");
        Console.WriteLine($"  -> incoming CRIT skill damage (has inflicted...on you), prefix stripped: {critIncomingSkillOk}");
        Console.WriteLine($"  -> incoming CRIT basic damage (received...from), prefix stripped: {critIncomingBasicOk}");
        Console.WriteLine($"  -> reflected damage, correctly attributed to the mob, not \"Your attack...\": {reflectOk}");
        Console.WriteLine($"  -> heal-other (You restored...of X's HP): {healOtherOk}");
        Console.WriteLine($"  -> self-heal for a NAMED character, not just You: {healSelfOtherCharacterOk}");
        Console.WriteLine($"  -> heal-by-other, third person: {healByOtherThirdPersonOk}");
        Console.WriteLine($"  -> heal-by-other, first person with \"on you\": {healByOtherOnYouOk}");
        Console.WriteLine($"  -> no You/you identity split across any of the above: {noIdentitySplit}");
        Console.WriteLine($"  -> no \"Critical Hit!\" prefix leaked into any captured name: {noCriticalHitLeak}");
        Console.WriteLine($"  -> unattributed DoT-tick lines correctly produced no events: {dotLinesProducedNoEvents}");
        Console.WriteLine($"  -> LiveAggregator.Summarize excludes pure healers from the damage list: {healersExcludedFromDamageSummary}");
        Console.WriteLine($"    {summary}");

        return critWithSkillOk && critWordOk && incomingSkillOk && incomingBasicOk
            && critIncomingSkillOk && critIncomingBasicOk && reflectOk
            && healOtherOk && healSelfOtherCharacterOk && healByOtherThirdPersonOk && healByOtherOnYouOk
            && noIdentitySplit && noCriticalHitLeak && dotLinesProducedNoEvents && healersExcludedFromDamageSummary;
    }
}
