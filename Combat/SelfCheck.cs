using AionSniffer.Protocol;

namespace AionSniffer.Combat;

/// <summary>
/// Reproduces two known scenarios against <see cref="DpsCalculator"/> so the formulas can be
/// verified without a live capture: the Gladiator/Zauberer "ALL view" thought experiment that
/// motivated distinguishing DPS from iDPS in the first place, and the real numbers pulled from
/// a public myaion.eu boss-fight session (see README) to ground-truth the iDPS formula.
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

        double gladWall = DpsCalculator.AllDpsWallClock(events, GladiatorId);
        double zaubWall = DpsCalculator.AllDpsWallClock(events, ZaubererId);
        double? gladActive = DpsCalculator.AllDpsActiveOnly(events, GladiatorId, TimeSpan.FromSeconds(3));
        double? zaubActive = DpsCalculator.AllDpsActiveOnly(events, ZaubererId, TimeSpan.FromSeconds(3));

        Console.WriteLine("[selftest] Gladiator/Zauberer ALL-view scenario:");
        Console.WriteLine($"  Gladiator: wallclock={gladWall:F1} active={Fmt(gladActive)} (300 hits x 1000 dmg, 1s apart)");
        Console.WriteLine($"  Zauberer:  wallclock={zaubWall:F1} active={Fmt(zaubActive)} (5 hits x 50000 dmg, 60s apart)");

        // Expectation: on a raw wall-clock "ALL" reading the bursty Zauberer is NOT obviously
        // behind the sustained Gladiator (~1042 vs ~1003) -- this is the exact unfairness the
        // user's original example was about.
        bool wallClockLooksUnfair = zaubWall > gladWall * 0.9;

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
    /// writeImpl would (see CombatPacketParser.TryParseAttack's doc comment), with two hits: one
    /// plain hit (shieldType 0, no extra fields) and one protected hit (shieldType 8, +12 bytes),
    /// to verify the parser actually walks the variable-length list and picks the right extra
    /// block size instead of assuming a fixed layout.
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
        body.Add(2); // hitCount

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

        body.Add(0); // trailing list-size byte

        var parsed = CombatPacketParser.TryParseAttack(body.ToArray());

        Console.WriteLine("[selftest] SM_ATTACK multi-hit parsing (synthetic, 1 plain + 1 shielded hit):");
        Console.WriteLine($"  parsed: {(parsed is null ? "null" : CombatPacketParser.TryDescribeAttack(body.ToArray()))}");

        bool structureOk = parsed is not null
            && parsed.Hits.Count == 2
            && parsed.AttackerObjectId == attacker
            && parsed.TargetObjectId == target
            && parsed.TargetHpPercent == targetHp
            && parsed.AttackerHpPercent == attackerHp;
        bool hit1Ok = parsed is not null && parsed.Hits[0] == new CombatPacketParser.AttackHit(1000, 5, 0);
        bool hit2Ok = parsed is not null && parsed.Hits[1] == new CombatPacketParser.AttackHit(2000, 3, 8);

        Console.WriteLine($"  -> header fields correct: {structureOk}");
        Console.WriteLine($"  -> hit 1 (plain) correct: {hit1Ok}");
        Console.WriteLine($"  -> hit 2 (shielded, correctly skipped 12 extra bytes to find hit 2): {hit2Ok}");

        return structureOk && hit1Ok && hit2Ok;
    }

    private static void WriteI32(List<byte> buf, int value) => buf.AddRange(BitConverter.GetBytes(value));
    private static void WriteU16(List<byte> buf, ushort value) => buf.AddRange(BitConverter.GetBytes(value));
}
