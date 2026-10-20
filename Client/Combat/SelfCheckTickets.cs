using System.IO;
using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The ticket frames (0x0b61 list, 0x0c61 change): read from real frames of the debug recording of 2026-10-13 (Aahz, login 17:11:54).</summary>
public static class SelfCheckTickets
{
    // the login list of the character Aahz: 73 entries, 492 bytes
    public const string SampleLogin = "0b61490c010000000a0101030000008085800100000000040400000002000600000004070000001c04080000000201090000008085800100000000040a0000000e040b00000002040c00000002010d0000008085800100000000040e0000000e040f0000000e006500000004660000000104670000000101c9000000808580010000000001ca000000808580010000000001cb000000808580010000000001cc000000808580010000000001cd000000808580010000000001ce0000008085800100000000008196980000829698000083969800008496980000859698000086969800008796980000889698000089969800008a969800008b969800008c96980004018793039c0404028793030204038793030804048793030804058793030804068793030304078793032304088793031c04098793030a040a8793030704658793030804c987930307044d8b93030704b18b93030804358f93030704998f930308041d9393030704819393030804059793030704699793030704b59b930302049d9f93030204bda29303070421a3930308048daa93030704f1aa9303080445b693030704a9b6930307042dba9303070415be93030704fdc193030704811d2c04030401b4c4040304814a5d050404824a5d050104834a5d050404844a5d050204854a5d050404864a5d0502";

    public static bool Run()
    {
        Console.WriteLine("[selftest] Tickets (login list, changes, which character they belong to, refills):");
        ServerTime.UseDatesAsServerTimes(); // the dates below are read as the game's own clock
        byte[] list = Convert.FromHexString(SampleLogin);
        bool parsed = Aion2Tickets.TryParseList(list, out List<TicketReading> entries);
        TicketValue odyle = entries.FirstOrDefault(e => e.Id == Aion2Tickets.OdyleId).Value;
        bool listOk = parsed && entries.Count == 73
            && odyle.Count == 540 && odyle.Bonus == 0
            && entries.First(e => e.Id == 7).Value.Count == 28
            && entries.First(e => e.Id == 60000007).Value.Count == 35
            && entries.First(e => e.Id == 3).Value.TimeMs == 25_200_000
            && entries.First(e => e.Id == 1).Value is { Count: 10, Bonus: 1 };
        bool truncated = !Aion2Tickets.TryParseList(list.AsSpan(0, list.Length - 1), out _);
        Console.WriteLine($"  -> login list: {entries.Count} entries, Odyle {odyle.Count}+{odyle.Bonus}, keys 28, boss kills 35, 7 h access time: {listOk}; a frame cut short is refused: {truncated}");

        // changes: Odyle bonus 630 -> 640 with +10 gained (flag 1), and daily 25 with bonus 520 after a use (reason 3)
        bool gain = Aion2Tickets.TryParseUpdate(Convert.FromHexString("0c610108018793038005010a000000"), out TicketReading g, out int reasonG, out int gained)
            && g.Id == Aion2Tickets.OdyleId && g.Value.Bonus == 640 && g.Value.Count == 0 && reasonG == 1 && gained == 10;
        bool used = Aion2Tickets.TryParseUpdate(Convert.FromHexString("0c61000c0187930319880403"), out TicketReading u, out int reasonU, out _)
            && u.Id == Aion2Tickets.OdyleId && u.Value.Count == 25 && u.Value.Bonus == 520 && reasonU == 3;
        bool refill = Aion2Tickets.TryParseUpdate(Convert.FromHexString("0c6100040a0000000e02"), out TicketReading r, out int reasonR, out _) && r.Id == 10 && r.Value.Count == 14 && reasonR == 2;
        Console.WriteLine($"  -> changes: gain with +10={gain}, use={used}, refill={refill}");

        // which character: the login frames between the list and the character record announce the OLD character again
        Aion2Tickets.Reset();
        var t0 = new DateTime(2026, 10, 13, 17, 11, 54);
        Aion2Tickets.NoteCharacter("Chumley", 1304, t0.AddSeconds(-600));
        Aion2Tickets.ReceiveList(entries, t0);
        Aion2Tickets.NoteCharacter("Chumley", 1304, t0.AddSeconds(-600));   // an old announcement in the gap: must not take the list
        bool notYet = Aion2Tickets.Snapshot().Count == 0;
        Aion2Tickets.ReceiveUpdate(new TicketReading(Aion2Tickets.OdyleId, new TicketValue(500, 0, 0)), t0.AddSeconds(1)); // waits with the list
        Aion2Tickets.NoteCharacter("Aahz", 1304, t0.AddSeconds(4));          // the character record of the new login
        TicketCharacter? aahz = Aion2Tickets.Snapshot().FirstOrDefault(c => c.Name == "Aahz");
        bool bound = aahz is not null && Aion2Tickets.CurrentName == "Aahz" && aahz.Tickets[Aion2Tickets.OdyleId].Count == 500 && aahz.Tickets[7].Count == 28;
        Aion2Tickets.ReceiveUpdate(new TicketReading(7, new TicketValue(27, 0, 0)), t0.AddSeconds(60));
        bool changed = Aion2Tickets.Snapshot().First(c => c.Name == "Aahz").Tickets[7].Count == 27;
        Console.WriteLine($"  -> old announcement takes nothing={notYet}, list bound to the new character with the change that followed it={bound}, later change applied={changed}");

        // refills since the last sight: daily +2 up to 14 (16:00), weekly 35 on Wednesday 16:00
        TicketDef? nightmare = Aion2Tickets.Find(4);
        TicketDef? kills = Aion2Tickets.Find(60000007);
        var seen = new DateTime(2026, 10, 12, 10, 0, 0); // a Monday
        bool refills = nightmare is not null && kills is not null
            && Aion2Tickets.Expected(nightmare, 3, seen, new DateTime(2026, 10, 14, 17, 0, 0)) == 9
            && Aion2Tickets.Expected(nightmare, 13, seen, new DateTime(2026, 10, 14, 17, 0, 0)) == 14
            && Aion2Tickets.Expected(kills, 2, new DateTime(2026, 10, 14, 15, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 35
            && Aion2Tickets.Expected(kills, 2, new DateTime(2026, 10, 14, 17, 0, 0), new DateTime(2026, 10, 15, 17, 0, 0)) == 2
            && Aion2Tickets.NextReset(kills, new DateTime(2026, 10, 14, 17, 0, 0)) == new DateTime(2026, 10, 21, 16, 0, 0)
            && Aion2Tickets.NextReset(nightmare, new DateTime(2026, 10, 14, 17, 0, 0)) == new DateTime(2026, 10, 15, 16, 0, 0);
        // the daily dungeon: 14 new entries every Wednesday 16:00 game time (09:00 in Central Europe in summer), whatever was left; the extra entries stay behind it
        TicketDef? dungeon = Aion2Tickets.Find(101);
        bool dungeonRefill = dungeon is not null
            && Aion2Tickets.Expected(dungeon, 3, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 14
            && Aion2Tickets.Expected(dungeon, 5, new DateTime(2026, 10, 7, 17, 0, 0), new DateTime(2026, 10, 14, 15, 0, 0)) == 5
            && Aion2Tickets.Expected(dungeon, 3, new DateTime(2026, 10, 14, 17, 0, 0), new DateTime(2026, 10, 20, 17, 0, 0)) == 3;
        // what the player confirmed: Shugo +3 and Nightmare +2 every day, the Ascension rite 3 every Wednesday, all at 16:00 game time
        TicketDef? shugo = Aion2Tickets.Find(1);
        TicketDef? awaken = Aion2Tickets.Find(80000001);
        bool rules = shugo is not null && awaken is not null && nightmare is not null
            && Aion2Tickets.Expected(shugo, 6, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 9
            && Aion2Tickets.Expected(shugo, 19, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 21
            && Aion2Tickets.Expected(nightmare, 1, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 3
            && Aion2Tickets.Expected(awaken, 1, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0)) == 3
            && Aion2Tickets.Expected(awaken, 1, new DateTime(2026, 10, 14, 17, 0, 0), new DateTime(2026, 10, 20, 17, 0, 0)) == 1;
        // the membership frame: eight effects with one end (31 October 15:18:46 local, 16 days 20 hours left on the recording day)
        string effect = "04000000" + "a9a2aef2a0010000" + "a9c65392a1010000";
        string membershipFrame = "3be3" + "08" + string.Concat(new[] { "22", "23", "25", "26", "27", "28", "29", "2a" }.Select(id => id + effect));
        bool membership = Aion2Tickets.TryParseMembership(Convert.FromHexString(membershipFrame), out TicketReading member)
            && member.Id == Aion2Tickets.PremiumId && member.Value.TimeMs > 1_700_000_000_000;
        var withPremium = new TicketCharacter("Member", 1304, DateTime.UtcNow, new Dictionary<int, TicketValue> { [Aion2Tickets.PremiumId] = member.Value }, 45);
        long endMs = member.Value.TimeMs;
        bool premiumKnown = Aion2Tickets.HasPremium(withPremium, DateTimeOffset.FromUnixTimeMilliseconds(endMs - 1000).UtcDateTime) == true
            && Aion2Tickets.HasPremium(withPremium, DateTimeOffset.FromUnixTimeMilliseconds(endMs + 1000).UtcDateTime) == false
            && Aion2Tickets.HasPremium(new TicketCharacter("Old", 1304, DateTime.UtcNow, new Dictionary<int, TicketValue>()), DateTime.UtcNow) is null
            && !Aion2Tickets.TryParseMembership(Convert.FromHexString("3be30800"), out _);
        bool plainCaps = shugo is not null && Aion2Tickets.Expected(shugo, 11, new DateTime(2026, 10, 13, 17, 0, 0), new DateTime(2026, 10, 14, 17, 0, 0), premium: false) == 12;
        Console.WriteLine($"  -> membership frame read (eight effects, one end)={membership}, premium known / over / unknown={premiumKnown}, caps without it (12 keys)={plainCaps}");
        // the pet auto-loot ticket (entry 44 of the membership frame) and the Daeva passes (frame 0x24e3), real frames of 19 October 2026: Nunzio's login at 16:31
        // (the ticket ran until 17 November 21:45, the game showed 29 d 5 h; the passes 22,720 and 10,190), Skeeve's change at 16:55 (second pass 2,100 / 240)
        const string withTicket = "3be3092204000000a9a2aef2a0010000a9c65392a10100002304000000a9a2aef2a0010000a9c65392a10100002504000000a9a2aef2a0010000a9c65392a10100002604000000a9a2aef2a0010000a9c65392a10100002704000000a9a2aef2a0010000a9c65392a10100002804000000a9a2aef2a0010000a9c65392a10100002904000000a9a2aef2a0010000a9c65392a10100002a04000000a9a2aef2a0010000a9c65392a10100002c0800000083328c50a101000083fa0aeba1010000";
        bool ticketFrame = Aion2Tickets.TryParseMembershipFrame(Convert.FromHexString(withTicket), out List<TicketReading> ticketReadings)
            && ticketReadings.Count == 2
            && ticketReadings.Single(r => r.Id == Aion2Tickets.PremiumId).Value.TimeMs == member.Value.TimeMs
            && Math.Abs(ticketReadings.Single(r => r.Id == Aion2Tickets.AutoLootId).Value.TimeMs - new DateTimeOffset(2026, 11, 17, 21, 45, 0, TimeSpan.FromHours(2)).ToUnixTimeMilliseconds()) < 60_000;
        string benefits = withTicket.Substring(6, 8 * 42);
        bool noTicket = Aion2Tickets.TryParseMembershipFrame(Convert.FromHexString("3be308" + benefits), out List<TicketReading> noTicketReadings)
            && noTicketReadings.Single(r => r.Id == Aion2Tickets.AutoLootId).Value.TimeMs == 0
            && noTicketReadings.Single(r => r.Id == Aion2Tickets.PremiumId).Value.TimeMs == member.Value.TimeMs;
        // a frame with the ticket alone (a ticket was used) leaves the membership alone; an empty list means neither; a broken length is refused
        bool ticketAlone = Aion2Tickets.TryParseMembershipFrame(Convert.FromHexString("3be301" + withTicket.Substring(6 + 8 * 42, 42)), out List<TicketReading> aloneReadings)
            && aloneReadings.Count == 1 && aloneReadings[0].Id == Aion2Tickets.AutoLootId && aloneReadings[0].Value.TimeMs > 1_700_000_000_000
            && Aion2Tickets.TryParseMembershipFrame(Convert.FromHexString("3be300"), out List<TicketReading> emptyReadings) && emptyReadings.Count == 2 && emptyReadings.All(r => r.Value.TimeMs == 0)
            && !Aion2Tickets.TryParseMembershipFrame(Convert.FromHexString("3be30900"), out _);
        bool passFrame = Aion2Tickets.TryParsePass(Convert.FromHexString("24e3023c270000c0580000c05800001efe8754a101000000fff8b9c6ffffff013687010001368701000e0236870100e80a030037870100e80a03000a010000003d270000ce270000ce27000012328854a1010000000776aacafeffff0148a607000148a607000e0248a60700409c000049a60700409c000001000000"), out List<TicketReading> passReadings)
            && passReadings.Count == 2
            && passReadings.Single(r => r.Id == Aion2Tickets.Passes[0].TicketId).Value is { Count: 22720, Bonus: 22720 }
            && passReadings.Single(r => r.Id == Aion2Tickets.Passes[1].TicketId).Value is { Count: 10190, Bonus: 10190 }
            && Aion2Tickets.TryParsePass(Convert.FromHexString("24e3013d27000034080000f00000005f1e0c54a101000000a70a4e02feffff0148a607000148a60700020248a60700409c000049a60700409c00000201000000"), out List<TicketReading> passChange)
            && passChange.Count == 1 && passChange[0].Id == Aion2Tickets.Passes[1].TicketId && passChange[0].Value is { Count: 2100, Bonus: 240 }
            && !Aion2Tickets.TryParsePass(Convert.FromHexString("24e300"), out _);
        // an Asmodian's login (Kijin, 20 October 2026): the passes carry the ids 20044 and 20045 and land on the same two tickets; the name follows the character's side
        bool asmodianPass = Aion2Tickets.TryParsePass(Convert.FromHexString("24e3024c4e00004eb00100e2b80000c9189e4da1010000000e2c918d01000001d60d030001d60d03003e02d60d0300e80a0300d70d0300e80a03000a010000004d4e000072970000983a000047b60548a101000000367c6e2bffffff01e82c090001e82c09003202e82c0900409c0000e92c0900409c000001000000"), out List<TicketReading> asmoReadings)
            && asmoReadings.Count == 2
            && asmoReadings.Single(r => r.Id == Aion2Tickets.Passes[0].TicketId).Value is { Count: 110670, Bonus: 47330 }
            && asmoReadings.Single(r => r.Id == Aion2Tickets.Passes[1].TicketId).Value is { Count: 38770, Bonus: 15000 }
            && Aion2Tickets.Passes[0].NameKeyFor(2321) == "Checklist.PassDark1" && Aion2Tickets.Passes[0].NameKeyFor(1304) == "Checklist.PassLight1"
            && Aion2Tickets.Passes[1].NameKeyFor(2303) == "Checklist.PassDark2" && Aion2Tickets.Passes[1].NameKeyFor(0) == "Checklist.PassLight2";
        passFrame = passFrame && asmodianPass;
        // the account key of the character list: Elyos (2a 00 00 00 01 02 01) and Asmodian (2c 00 00 00 02 02 01) heads cut from real lists of 19 and 20 October 2026
        static byte[] ListHead(string hex) => Convert.FromHexString(hex).Concat(new byte[40]).ToArray();
        bool accountKeys = AionDPS.Aion2.Protocol.Aion2FrameDecoder.TryReadAccountKey(ListHead("2636000005832200000c24716378566b3155464270662a000000010201"), out string elyosKey) && elyosKey == "$qcxVk1UFBpf"
            && AionDPS.Aion2.Protocol.Aion2FrameDecoder.TryReadAccountKey(ListHead("2636000005832200000c244d57663551596166727a352c000000020201"), out string asmoKey) && asmoKey == "$MWf5QYafrz5"
            && !AionDPS.Aion2.Protocol.Aion2FrameDecoder.TryReadAccountKey(ListHead("2636000005832200000c244d57663551596166727a352c000000030201"), out _)
            && AionDPS.Aion2.Protocol.Aion2SkillNames.FactionOfCode(14) == "Elyos" && AionDPS.Aion2.Protocol.Aion2SkillNames.FactionOfCode(13) == "Elyos" && AionDPS.Aion2.Protocol.Aion2SkillNames.FactionOfCode(15) == "Asmodian"
            && AionDPS.Aion2.Protocol.Aion2SkillNames.FactionOfCode(16) == "Asmodian" && AionDPS.Aion2.Protocol.Aion2SkillNames.FactionOfCode(0) is null;
        passFrame = passFrame && accountKeys;
        // the levels as the game showed them: first pass 15 (1,520 of 1,600), 16 (280), 15 (1,540); second pass 15 (460 of 700), 8 (210), 4 (0); above the known levels no answer
        int first = Aion2Tickets.Passes[0].TicketId, second = Aion2Tickets.Passes[1].TicketId;
        bool passLevels = Aion2Tickets.PassLevelOf(first, 22720) == new Aion2Tickets.PassLevel(15, 1520, 1600)
            && Aion2Tickets.PassLevelOf(first, 23080) == new Aion2Tickets.PassLevel(16, 280, 1600)
            && Aion2Tickets.PassLevelOf(first, 22740) == new Aion2Tickets.PassLevel(15, 1540, 1600)
            && Aion2Tickets.PassLevelOf(first, 19110) == new Aion2Tickets.PassLevel(13, 1110, 1600)
            && Aion2Tickets.PassLevelOf(second, 10260) == new Aion2Tickets.PassLevel(15, 460, 700)
            && Aion2Tickets.PassLevelOf(second, 5110) == new Aion2Tickets.PassLevel(8, 210, 700)
            && Aion2Tickets.PassLevelOf(second, 2100) == new Aion2Tickets.PassLevel(4, 0, 700)
            && Aion2Tickets.PassLevelOf(first, 150000) is null && Aion2Tickets.PassLevelOf(second, 45000) is null && Aion2Tickets.PassLevelOf(12345, 10) is null;
        // the warning of the checklist: what the next daily refill brings beyond the cap is lost (Nightmare 14, Shugo 12 / 21 with the membership)
        bool lostTomorrow = shugo is not null && nightmare is not null && dungeon is not null
            && Aion2Tickets.LostAtNextRefill(nightmare, 13, true) == 1 && Aion2Tickets.LostAtNextRefill(nightmare, 14, true) == 2 && Aion2Tickets.LostAtNextRefill(nightmare, 12, true) == 0
            && Aion2Tickets.LostAtNextRefill(shugo, 20, true) == 2 && Aion2Tickets.LostAtNextRefill(shugo, 18, true) == 0 && Aion2Tickets.LostAtNextRefill(shugo, 11, false) == 2
            && Aion2Tickets.LostAtNextRefill(dungeon, 14, true) == 0;
        Console.WriteLine($"  -> pet auto-loot ticket read / none / alone / empty={ticketFrame}/{noTicket}/{ticketAlone}, Daeva pass frames={passFrame}, pass levels={passLevels}, lost at the next refill={lostTomorrow}");
        // game time is Korean time: 16:00 there is 07:00 UTC
        ServerTime.Reset();
        bool korean = ServerTime.FromLocal(new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc)) == new DateTime(2026, 10, 14, 16, 0, 0)
            && ServerTime.ToLocal(new DateTime(2026, 10, 14, 16, 0, 0)).ToUniversalTime() == new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc);
        ServerTime.UseDatesAsServerTimes();
        Console.WriteLine($"  -> Shugo +3 / Nightmare +2 daily and Ascension 3 on Wednesdays={rules}, daily dungeon back to 14 every Wednesday 16:00 game time={dungeonRefill}, game time is Korean time (16:00 there = 07:00 UTC)={korean}");
        Console.WriteLine($"  -> refills (daily +2 up to the cap, weekly set on Wednesday 16:00): {refills}; the table has {Aion2Tickets.Catalog.Count} tickets, German name of Odyle energy: {Aion2Tickets.Find(Aion2Tickets.OdyleId)?.NameIn("de")}");

        // the cooldown list 0x4738 (17:26:37, Aahz): 25 entries, Fury-type skill 11,250,000 with 109,500 left, the dodge 11,000,100 ready, 11,190,000 with 10,450
        const string cooldowns = "4738196041ae00004d0400000077230000004e040000007cc54a000050a9ab00bcd70624d9a700000075a80000f0beaa00d251605fa90000801eac0000e008ad0090f40430ccad00e0c10140f3ad00e6a0039045ac0000a06cac0000808fae0000d070aa0000d0ffa70000f04da80000e026a8000000e6aa000020c3a80000109ca800000057ad0000";
        byte[] cd = Convert.FromHexString(cooldowns);
        bool cdParsed = Aion2Cooldowns.TryParse(cd, out List<CooldownEntry> cdEntries);
        bool cooldownOk = cdParsed && cdEntries.Count == 25
            && cdEntries.First(e => e.Id == 11250000).Remaining == 109500
            && cdEntries.First(e => e.Id == 11000100).Remaining == 0
            && cdEntries.First(e => e.Id == 11190000).Remaining == 10450
            && !Aion2Cooldowns.TryParse(cd.AsSpan(0, cd.Length - 1), out _);
        Aion2Cooldowns.Reset();
        Aion2Cooldowns.Receive(cdEntries, t0);
        Aion2Cooldowns.Receive(new List<CooldownEntry> { new(11100000, 40600) }, t0.AddSeconds(2));   // a short frame names one skill; the rest stays
        (IReadOnlyList<CooldownEntry> kept, _) = Aion2Cooldowns.Last;
        bool shortKeeps = kept.Count == 25 && kept.First(e => e.Id == 11100000).Remaining == 40600 && kept.First(e => e.Id == 11250000).Remaining == 109500;
        Aion2Cooldowns.Reset();
        Console.WriteLine($"  -> cooldown list 0x4738: 25 entries and the numbers read={cooldownOk}, a short frame changes only its skill={shortKeeps}");

        // the hand-ticked part: count of the five daily tasks (cleared at 16:00) and the weekly scroll counters (cleared Wednesday 16:00)
        string tmp = Path.Combine(Path.GetTempPath(), $"checklist-{Guid.NewGuid():N}.json");
        ChecklistManual.PathOverride = tmp;
        ChecklistManual.Reset();
        var wed = new DateTime(2026, 10, 14, 17, 0, 0); // a Wednesday after the reset
        ChecklistManual.Step("Aahz", ChecklistManual.Counter.Verteron, 5, wed);
        ChecklistManual.Step("Aahz", ChecklistManual.Counter.Verteron, 10, wed);   // stops at 12
        ChecklistManual.Step("Aahz", ChecklistManual.Counter.Abyss, 3, wed);
        ChecklistManual.Step("Aahz", ChecklistManual.Counter.Abyss, -9, wed);  // stops at 0
        ChecklistManual.State same = ChecklistManual.Get("Aahz", wed.AddHours(1));
        bool ticks = same.Verteron == 12 && same.Abyss == 0;
        ChecklistManual.Reset(); // read back from the file
        ChecklistManual.State next = ChecklistManual.Get("Aahz", new DateTime(2026, 10, 15, 15, 0, 0)); // Thursday 15:00: before the 16:00 of the day, after Wednesday's
        bool keptUntilReset = next.Verteron == 12;          // daily: the reset of Thursday 16:00 has not come yet
        ChecklistManual.State daily = ChecklistManual.Get("Aahz", new DateTime(2026, 10, 15, 16, 1, 0));
        bool dailyCleared = daily.Verteron == 12; // the weekly counter stays
        ChecklistManual.State weekly = ChecklistManual.Get("Aahz", new DateTime(2026, 10, 21, 16, 1, 0));
        bool weeklyCleared = weekly.Verteron == 0 && weekly.Abyss == 0;
        bool resetTimes = ChecklistManual.LastDailyReset(new DateTime(2026, 10, 14, 15, 59, 0)) == new DateTime(2026, 10, 13, 16, 0, 0)
            && ChecklistManual.LastWeeklyReset(new DateTime(2026, 10, 14, 15, 59, 0)) == new DateTime(2026, 10, 7, 16, 0, 0)
            && ChecklistManual.LastWeeklyReset(new DateTime(2026, 10, 14, 16, 0, 0)) == new DateTime(2026, 10, 14, 16, 0, 0);
        ChecklistManual.PathOverride = null;
        ChecklistManual.Reset();
        try { File.Delete(tmp); } catch (IOException) { }
        Console.WriteLine($"  -> hand-ticked part: ticks and counters (stop at 12 and 0)={ticks}, kept until 16:00={keptUntilReset}, daily cleared and weekly kept={dailyCleared}, weekly cleared next Wednesday={weeklyCleared}, reset times={resetTimes}");

        // the quest list 0x1c8d: taken and handed-in missions of the day (head of the frame at 16:21:00 on 14 October: 3 taken in the list head, 5 handed in; on 17 October the first number reached 6 with only five missions accepted)
        bool missionsRead = Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d030000000500000011150200"), out TicketReading missions)
            && missions.Id == Aion2Tickets.MissionsId && missions.Value.Count == 5 && missions.Value.Bonus == 3
            && !Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d150000000500000011150200"), out _)
            && Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d060000000500000013850200"), out TicketReading sixTaken)
            && sixTaken.Value.Count == 5 && sixTaken.Value.Bonus == 6
            && !Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d0300"), out _);
        Console.WriteLine($"  -> mission counter from the quest list 0x1c8d: {missionsRead}");

        // the shop counters 0x8e56 (12:38:27 on 14 October: the 20th purchase of Odyle energy; counter 100100 = another item, not read)
        bool shopRead = Aion2Tickets.TryParseShopCounters(Convert.FromHexString("8e5600000126870100140000008079425da1010000"), out TicketReading shop)
            && shop.Id == Aion2Tickets.ShopOdyleId && shop.Value.Count == 20 && shop.Value.TimeMs == 1792566000000
            && !Aion2Tickets.TryParseShopCounters(Convert.FromHexString("8e5600000104870100" + "0a0000008079425da1010000"), out _)
            && !Aion2Tickets.TryParseShopCounters(Convert.FromHexString("8e560000"), out _);
        Console.WriteLine($"  -> shop counter of the Odyle purchase 0x8e56: {shopRead}");

        // the level a counter needs: below it the checklist greys it out (unknown level = unlocked)
        var low = new TicketCharacter("Low", 1304, DateTime.UtcNow, new Dictionary<int, TicketValue>(), 28);
        var high = low with { Level = 45 };
        var unknown = low with { Level = 0 };
        bool locks = Aion2Tickets.IsLocked(low, 101) && Aion2Tickets.IsLocked(low, 4) && Aion2Tickets.IsLocked(low, 80000001) && Aion2Tickets.IsLocked(low, 8)
            && !Aion2Tickets.IsLocked(low, Aion2Tickets.OdyleId) && !Aion2Tickets.IsLocked(low, 1) && !Aion2Tickets.IsLocked(high, 4) && !Aion2Tickets.IsLocked(unknown, 4)
            && Aion2Tickets.UnlockLevel(101) == 30 && Aion2Tickets.UnlockLevel(Aion2Tickets.OdyleId) == 22;
        Console.WriteLine($"  -> counters locked by level (daily dungeon 30, Nightmare/Ascension/Invasion 45, Odyle 22, Shugo 13)={locks}");

        // the character list of the login (0x2636): every character of the account with its basics, cut out of the real list of 19 October 2026 (two of the four
        // characters of one account; the game showed Aahz at level 45 with item level 2,661 and Skeeve at level 22 with 318)
        string zeros = new('0', 160);
        List<AccountCharacter> account = AccountCharacterList.Parse(Convert.FromHexString("26360000" + "00000000ca080000044161687a0600000001022d00000000000000650a0000000000000000000001000000000000000000000008416b617473756b695eea9254a1010000ea917b47bac1964600b6f546f8070000e1b5a9f2a0010000" + zeros + "353b03000000180500000000f649824fa10100002cd8cc0100000000" + "00000000c70e000006536b656576651a000000010216000000000000003e01000025140c000000000001000000000000000000000008416b617473756b69c8351a54a1010000d910b3c60ff23047009065462908000019460e06a1010000" + zeros + "d6c303000000180500000000f649824fa1010000b4d2cc0100000000"));
        bool listRead = account.Count == 2
            && account[0] is { Name: "Aahz", ClassCode: 6, Faction: 1, Level: 45, ItemLevel: 2661, Legion: "Akatsuki", ServerId: 1304, CharacterId: 211765, LastOnlineMs: 1792420276830, CreatedMs: 1790777603553 }
            && account[1] is { Name: "Skeeve", ClassCode: 26, Faction: 1, Level: 22, ItemLevel: 318, Legion: "Akatsuki", ServerId: 1304, CharacterId: 246742, LastOnlineMs: 1792412366280, CreatedMs: 1791102961177 }
            && AccountCharacterList.Parse(Convert.FromHexString("26360000000000")).Count == 0;
        // the list gives every character of the account an entry in the checklist (without counters); a deleted one that only the list showed goes again,
        // one that has counters stays; a login keeps what the list told
        Aion2Tickets.Reset();
        Aion2Tickets.NoteListedCharacters("acct", account);
        IReadOnlyList<TicketCharacter> listed = Aion2Tickets.Snapshot();
        bool listedEntries = listed.Count == 2 && listed.All(c => c.Tickets.Count == 0 && c.Account == "acct") && listed.Single(c => c.Name == "Skeeve").ItemLevel == 318;
        Aion2Tickets.NoteListedCharacters("acct", new[] { account[0] });
        bool deletedGone = Aion2Tickets.Snapshot().Count == 1 && Aion2Tickets.Snapshot()[0].Name == "Aahz";
        DateTime loginAt = new(2026, 10, 19, 17, 0, 0, DateTimeKind.Utc);
        Aion2Tickets.ReceiveList(new[] { new TicketReading(Aion2Tickets.PremiumId, new TicketValue(0, 0, 1_800_000_000_000)) }, loginAt);
        Aion2Tickets.NoteCharacter("Aahz", 1304, loginAt.AddSeconds(4), 45);
        TicketCharacter loggedIn = Aion2Tickets.Snapshot().Single(c => c.Name == "Aahz");
        Aion2Tickets.NoteListedCharacters("acct", new[] { account[1] }); // Aahz has counters now: stays although this list lacks him
        bool listKept = loggedIn.Tickets.Count == 1 && loggedIn.ClassCode == 6 && loggedIn.ItemLevel == 2661
            && Aion2Tickets.Snapshot().Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "Aahz", "Skeeve" });
        Console.WriteLine($"  -> character list read (two real entries: class, level, item level, legion, times, server, character id)={listRead}, entries for characters without counters={listedEntries}, deleted one goes={deletedGone}, login keeps the list's basics={listKept}");
        // what the server answers for another character (GET /api/players/:id, shaped like the real answer for Namì): the profile and NC's character view are read together
        const string serverAnswer = """
            {"player":{"id":119,"slug":"namitio-kaisinel","name":"Namì","guild":"Akatsuki","faction":"Elyos","serverId":4,"serverName":"Europe - Kaisinel"},
             "character":{"attributes":{"main":[{"id":1,"key":"str","value":39}],"lords":[{"id":7,"key":"justice","value":62}]},"wing":{"id":30200101},"wingSkin":{"id":30500101},
                          "activePet":{"id":1192,"level":2},"combatPower":116565,"itemLevel":2400},
             "profile":{"level":45,"className":"Cleric","averageItemLevel":70.1,"updatedAt":"2026-10-19 17:36:05","titles":[{"slot":1,"titleId":13010021}],
                        "skills":[{"id":17290000,"level":20,"baseLevel":20,"stigma":true,"equipped":false,"stones":null},{"id":11010000,"level":5,"baseLevel":5,"stigma":false,"equipped":true,"stones":[1,2]}],
                        "species":[{"id":2,"level":10,"progress":0,"effects":[{"page":1,"slot":0,"stat":19,"value":15,"kind":5}]}],"pets":[{"species":2,"level":10,"kinds":[5,1,2]}],
                        "daevanion":[{"board":71,"activePoints":113},{"board":72,"activePoints":20}],
                        "gear":[{"slot":1,"itemId":110730047,"enchant":15,"stones":[{"stat":104,"tier":2},{"stat":317,"tier":3}],"stats":[{"stat":282,"value":1220}],"godstone":{"itemId":19950004,"name":"Theostone"},
                                 "skillBonuses":[{"skill":11170000,"level":1}]},{"slot":3,"itemId":210330052}]}}
            """;
        using System.Text.Json.JsonDocument answer = System.Text.Json.JsonDocument.Parse(serverAnswer);
        AionDPS.Upload.RemoteCharacter? remote = AionDPS.Upload.RemoteCharacterClient.Parse(answer.RootElement);
        using System.Text.Json.JsonDocument bare = System.Text.Json.JsonDocument.Parse("{\"player\":{\"name\":\"Heimdall\"},\"character\":{\"itemLevel\":40}}");
        AionDPS.Upload.RemoteCharacter? bareRemote = AionDPS.Upload.RemoteCharacterClient.Parse(bare.RootElement);
        using System.Text.Json.JsonDocument nothing = System.Text.Json.JsonDocument.Parse("{\"error\":\"player_not_found\"}");
        bool remoteRead = remote is { Name: "Namì", Level: 45, ClassName: "Cleric", Faction: "Elyos", Guild: "Akatsuki", ItemLevel: 2400, CombatPower: 116565, WingId: 30200101, WingSkinId: 30500101, ActivePetId: 1192, ActivePetLevel: 2 }
            && Math.Abs(remote.AverageItemLevel - 70.1) < 0.001 && remote.Gear.Count == 2 && remote.Titles.Count == 1 && remote.Titles[0].TitleId == 13010021
            && remote.Gear[0] is { SlotIndex: 1, ItemId: 110730047, Enchant: 15, GodstoneId: 19950004 } weapon && weapon.Stones!.Count == 2 && weapon.Stones[1].StatId == 317 && weapon.Stones[1].Tier == 3
            && weapon.Stats![0].StatId == 282 && weapon.Stats[0].Value == 1220 && weapon.SkillBonuses![0].SkillId == 11170000
            && remote.Gear[1] is { SlotIndex: 3, Enchant: 0, GodstoneId: 0 }
            && remote.Attributes is { } values && values[1] == 39 && values[7] == 62
            && remote.Slug == "namitio-kaisinel" && remote.Skills!.Count == 2 && remote.Skills[0] is { SkillId: 17290000, Level: 20, Stigma: true, Equipped: false, Stones: null }
            && remote.Skills[1] is { SkillId: 11010000, Equipped: true } && remote.Skills[1].Stones!.SequenceEqual(new[] { 1, 2 })
            && remote.Species![0] is { SpeciesId: 2, Level: 10 } && remote.Species[0].Effects[0] is { Page: 1, Slot: 0, StatId: 19, Value: 15, Kind: 5 }
            && remote.Pets![0] is { SpeciesId: 2, Level: 10 } && remote.Pets[0].Kinds.SequenceEqual(new[] { 5, 1, 2 }) && remote.DaevanionPoints == 133
            && bareRemote is { Name: "Heimdall", ItemLevel: 40, Attributes: null } && bareRemote.Gear.Count == 0
            && AionDPS.Upload.RemoteCharacterClient.Parse(nothing.RootElement) is null;
        Console.WriteLine($"  -> the server's answer for another character read (equipment with stones and god stone, attributes, wings, pet, titles; a character without a profile; no player)={remoteRead}");
        Aion2Tickets.Reset();
        bool ok = remoteRead && listRead && listedEntries && deletedGone && listKept && membership && premiumKnown && plainCaps && ticketFrame && noTicket && ticketAlone && passFrame && passLevels && lostTomorrow && rules && dungeonRefill && korean && locks && missionsRead && ticks && keptUntilReset && dailyCleared && weeklyCleared && resetTimes && cooldownOk && shortKeeps && listOk && truncated && gain && used && refill && notYet && bound && changed && refills && Aion2Tickets.Catalog.Count == 70;
        Console.WriteLine(ok ? "  -> OK" : "  -> FAILED");
        ServerTime.Reset();
        return ok;
    }
}
