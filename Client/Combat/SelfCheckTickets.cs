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
        ChecklistManual.Step("Aahz", verteron: true, 5, wed);
        ChecklistManual.Step("Aahz", verteron: true, 10, wed);   // stops at 12
        ChecklistManual.Step("Aahz", verteron: false, 3, wed);
        ChecklistManual.Step("Aahz", verteron: false, -9, wed);  // stops at 0
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

        // the quest list 0x1c8d: taken and handed-in missions of the day (head of the frame at 16:21:00 on 14 October: 3 taken in the list head, 5 handed in)
        bool missionsRead = Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d030000000500000011150200"), out TicketReading missions)
            && missions.Id == Aion2Tickets.MissionsId && missions.Value.Count == 5 && missions.Value.Bonus == 3
            && !Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d090000000500000011150200"), out _)
            && !Aion2Tickets.TryParseMissions(Convert.FromHexString("1c8d0300"), out _);
        Console.WriteLine($"  -> mission counter from the quest list 0x1c8d: {missionsRead}");

        // the level a counter needs: below it the checklist greys it out (unknown level = unlocked)
        var low = new TicketCharacter("Low", 1304, DateTime.UtcNow, new Dictionary<int, TicketValue>(), 28);
        var high = low with { Level = 45 };
        var unknown = low with { Level = 0 };
        bool locks = Aion2Tickets.IsLocked(low, 101) && Aion2Tickets.IsLocked(low, 4) && Aion2Tickets.IsLocked(low, 80000001) && Aion2Tickets.IsLocked(low, 8)
            && !Aion2Tickets.IsLocked(low, Aion2Tickets.OdyleId) && !Aion2Tickets.IsLocked(low, 1) && !Aion2Tickets.IsLocked(high, 4) && !Aion2Tickets.IsLocked(unknown, 4)
            && Aion2Tickets.UnlockLevel(101) == 30 && Aion2Tickets.UnlockLevel(Aion2Tickets.OdyleId) == 22;
        Console.WriteLine($"  -> counters locked by level (daily dungeon 30, Nightmare/Ascension/Invasion 45, Odyle 22, Shugo 13)={locks}");

        Aion2Tickets.Reset();
        bool ok = locks && missionsRead && ticks && keptUntilReset && dailyCleared && weeklyCleared && resetTimes && cooldownOk && shortKeeps && listOk && truncated && gain && used && refill && notYet && bound && changed && refills && Aion2Tickets.Catalog.Count == 70;
        Console.WriteLine(ok ? "  -> OK" : "  -> FAILED");
        return ok;
    }
}
