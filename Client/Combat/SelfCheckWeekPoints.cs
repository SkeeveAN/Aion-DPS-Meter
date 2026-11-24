using AionDPS.Aion2;

namespace AionDPS.Combat;

/// <summary>The week points plan: the achievements frames (login and live), what counts as stale, held-back rewards, boss and siege gates, the plan itself.</summary>
public static class SelfCheckWeekPoints
{
    private const string Catalog = """
    {"tabs":["Abyss"],"tabNames":{"Abyss":{"en":"Abyss"}},"events":{"siege":{"days":[4,6],"at":[21.0]}},"missions":[
     {"id":1001,"race":"L","tab":"Abyss","kind":"KillNpcAll","steps":[{"step":1,"count":200,"points":110,"title":{"en":"Purify I"},"text":{"en":"Hunt 200"}},{"step":2,"count":400,"points":160,"title":{"en":"Purify II"},"text":{"en":"Hunt 400"}}]},
     {"id":1002,"race":"L","tab":"Abyss","kind":"KillNpc","npcs":[2600089],"steps":[{"step":1,"count":1,"points":90,"title":{"en":"Boss"},"text":{"en":"Boss"}},{"step":2,"count":5,"points":190,"title":{"en":"Boss II"},"text":{"en":"Boss x5"}}]},
     {"id":1003,"race":"L","tab":"Abyss","kind":"ElapsedTime","steps":[{"step":1,"count":60,"points":140,"title":{"en":"Time"},"text":{"en":"1 hour"}}]},
     {"id":1004,"race":"L","tab":"Abyss","kind":"AbyssArtifactEventComplete","event":"siege","steps":[{"step":1,"count":1,"points":90,"title":{"en":"Siege"},"text":{"en":"Siege"}}]}]}
    """;

    private static byte[] Frame(ushort opcodeLow, params (uint Id, uint Count, ulong Time, byte Level)[] entries)
    {
        var bytes = new List<byte> { (byte)(opcodeLow & 0xff), (byte)(opcodeLow >> 8), (byte)entries.Length };
        foreach ((uint id, uint count, ulong time, byte level) in entries)
        {
            bytes.AddRange(BitConverter.GetBytes(id));
            bytes.AddRange(BitConverter.GetBytes(count));
            bytes.AddRange(BitConverter.GetBytes(time));
            bytes.Add(level);
        }

        return bytes.ToArray();
    }

    private static long Ms(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    public static bool Run()
    {
        Console.WriteLine("[selftest] Week points (achievements frames, stale weeks, held-back rewards, boss and siege gates, the plan):");
        ServerTime.UseDatesAsServerTimes(); // the dates below are read as the game's own clock
        Aion2Missions.UseCatalogText(Catalog);
        try
        {
            // Tuesday evening; the week ends on Wednesday 16:00 game time
            var now = new DateTime(2026, 11, 24, 22, 34, 0);
            long fresh = Ms(now.AddHours(-3)), stale = Ms(now.AddDays(-8)); // before Wednesday 18 November 16:00, the start of this week

            // frames: the weekly points and the missions are kept, anything else is not; a frame cut short is refused
            byte[] login = Frame(0x8356, (100030000, 1, 1_791_499_543_290, 0), (1001, 38, (ulong)fresh, 0), (1003, 60, (ulong)fresh, 0), (1002, 0, 0, 0), (Aion2Tickets.WeeklyPointsAchievement, 2160, (ulong)fresh, 0));
            bool parsed = Aion2Missions.TryParseReadings(login, out List<TicketReading> readings)
                && readings.Count == 4 && readings.First(r => r.Id == 1001).Value is { Count: 38, Bonus: 0 } && readings.First(r => r.Id == 1003).Value.Count == 60
                && readings.First(r => r.Id == Aion2Tickets.WeeklyPointsId).Value.Count == 2160 && !readings.Any(r => r.Id == 100030000)
                && !Aion2Missions.TryParseReadings(login.Take(login.Length - 3).ToArray(), out _);
            byte[] live = Frame(0x8456, (1003, 60, (ulong)fresh, 1), (999, 5, (ulong)fresh, 0));
            bool liveOk = Aion2Missions.TryParseReadings(live, out List<TicketReading> changes) && changes.Count == 1 && changes[0].Id == 1003 && changes[0].Value.Bonus == 1;
            Console.WriteLine($"  -> the login's and the live frame keep the weekly points and missions, nothing else: {parsed && liveOk}");

            TicketCharacter Make(params (int Id, int Count, int Level, long Time)[] values)
            {
                var tickets = values.ToDictionary(v => v.Id, v => new TicketValue(v.Count, v.Level, v.Time));
                return new TicketCharacter("Test", 1301, DateTime.UtcNow, tickets);
            }

            DateTime thursday = new(2026, 11, 26, 21, 40, 0);
            BossStatus? Dead(int npc) => npc == 2600089 ? new BossStatus(BossPhase.Respawn, thursday, now) : null;
            BossStatus? Alive(int npc) => npc == 2600089 ? new BossStatus(BossPhase.Now, null, now) : null;

            TicketCharacter me = Make((1001, 38, 0, fresh), (1002, 0, 0, 0), (1003, 60, 0, fresh), (1004, 0, 0, 0), (Aion2Tickets.WeeklyPointsId, 2160, 0, fresh));
            WeekPlan plan = WeekPlanner.Build(me, now, 150, Dead);
            PlanRow? purify = plan.Rows.FirstOrDefault(r => r.Mission.Id == 1001 && r.State == PlanState.Plan);
            bool basics = plan.HasData && plan.Claimed == 2160 && plan.Waiting == 140 && plan.Reset == new DateTime(2026, 11, 25, 16, 0, 0)
                && plan.Rows.Any(r => r.State == PlanState.Waiting && r.Mission.Id == 1003 && r.Points == 140)
                && purify is { Count: 38, Target: 400, Points: 270, Steps: 2 } && Math.Abs(purify.Minutes - 362 * 0.09) < 0.01 && !purify.Passive
                && plan.Final == 2160 + 140 + 110 + 160 && plan.PlannedPoints == 270;
            Console.WriteLine($"  -> claimed 2160, waiting 140 (1 hour in the Abyss finished), Purify planned as one line of two steps (362 monsters), final {plan.Final}: {basics}");

            PlanRow? bossBlocked = plan.Rows.FirstOrDefault(r => r.Mission.Id == 1002);
            PlanRow? siegeBlocked = plan.Rows.FirstOrDefault(r => r.Mission.Id == 1004);
            bool gates = bossBlocked is { State: PlanState.Blocked, Why: "late" } && bossBlocked.When == thursday
                && siegeBlocked is { State: PlanState.Blocked, Why: "late", WhenIsDay: false } && siegeBlocked.When is { DayOfWeek: DayOfWeek.Thursday, Hour: 21 };
            WeekPlan alive = WeekPlanner.Build(me, now, 150, Alive);
            bool bossNow = alive.Rows.Any(r => r.Mission.Id == 1002 && r.State == PlanState.Plan && r.When is null && r.Step.Step == 1)
                && alive.Rows.Any(r => r.Mission.Id == 1002 && r.State == PlanState.Blocked && r.Why == "rare" && r.Step.Step == 2);
            WeekPlan unknown = WeekPlanner.Build(me, now, 150, _ => new BossStatus(BossPhase.Unknown, null, null));
            bool bossUnknown = unknown.Rows.Any(r => r.Mission.Id == 1002 && r.State == PlanState.Blocked && r.Why == "unknown");
            // two targets: one known to come after the reset, one the list says nothing about: not excluded
            string twoTargets = Catalog.Replace("\"npcs\":[2600089]", "\"npcs\":[2600089,2600097]");
            Aion2Missions.UseCatalogText(twoTargets);
            WeekPlan mixed = WeekPlanner.Build(me, now, 150, npc => npc == 2600089 ? Dead(npc) : new BossStatus(BossPhase.Unknown, null, null));
            bool oneUnknown = mixed.Rows.Any(r => r.Mission.Id == 1002 && r.State == PlanState.Blocked && r.Why == "unknown" && r.When is null);
            Aion2Missions.UseCatalogText(Catalog);
            bossUnknown = bossUnknown && oneUnknown;
            Console.WriteLine($"  -> a boss that comes after the reset and a siege on Thursday cannot be planned; a living boss can (step 2 needs 5: too rare); no list entry = unknown: {gates && bossNow && bossUnknown}");

            WeekPlan tight = WeekPlanner.Build(me, now, 10, Dead);
            bool budget = tight.PlannedPoints == 0 && tight.Rows.Any(r => r.Mission.Id == 1001 && r.State == PlanState.Spare) && tight.Final == 2160 + 140;
            Console.WriteLine($"  -> with 10 minutes the 15-minute step does not fit and stays 'possible, no time': {budget}");

            // after the reset the character has not seen: last week's counters are nothing, what was finished but not collected is still there (the game keeps it)
            TicketCharacter old = Make((1001, 150, 0, stale), (1003, 20, 0, stale), (Aion2Tickets.WeeklyPointsId, 2710, 0, stale));
            WeekPlan afterReset = WeekPlanner.Build(old, now, 150, Dead);
            PlanRow? from0 = afterReset.Rows.FirstOrDefault(r => r.Mission.Id == 1001 && r.State == PlanState.Plan);
            bool staleOk = afterReset.Claimed == 0 && afterReset.Waiting == 0 && from0 is { Count: 0, Steps: 2 } && Math.Abs(from0.Minutes - 400 * 0.09) < 0.01;
            TicketCharacter held = Make((1001, 450, 1, stale), (Aion2Tickets.WeeklyPointsId, 2710, 0, stale));
            WeekPlan keeps = WeekPlanner.Build(held, now, 150, Dead);
            bool heldOk = keeps.Waiting == 160 && keeps.Rows.Any(r => r.State == PlanState.Waiting && r.Mission.Id == 1001 && r.Step.Step == 2);
            Console.WriteLine($"  -> a stand from before the reset counts as 0 (400 monsters again); a finished, uncollected step survives it: {staleOk && heldOk}");

            // no data for the character: no plan
            bool none = !WeekPlanner.Build(Make((1, 5, 0, fresh)), now, 150, Dead).HasData;
            Console.WriteLine($"  -> a character without mission data gets no plan: {none}");

            // the real catalog of the game reads and holds the weekly missions of both races
            Aion2Missions.UseCatalogText(null);
            bool real = Aion2Missions.Missions.Count >= 40 && Aion2Missions.Missions.Any(m => m.Id == 230211100 && m.Steps.Count == 10 && m.Steps[0] is { Count: 200, Points: 110 })
                && Aion2Missions.Missions.Any(m => m.Race == "D") && Aion2Missions.Tabs.Count == 5;
            Console.WriteLine($"  -> the game's weekly missions are in the data file (both races, five tabs): {real}");
            return parsed && liveOk && basics && gates && bossNow && bossUnknown && budget && staleOk && heldOk && none && real;
        }
        finally
        {
            Aion2Missions.UseCatalogText(null);
        }
    }
}
