namespace AionDPS.Aion2;

/// <summary>What a mission step of the plan is: reward waiting to be collected, in the plan, fits no more into the time, or cannot be done before the reset.</summary>
public enum PlanState
{
    Waiting,
    Plan,
    Spare,
    Blocked,
}

/// <summary>One line of the week plan.</summary>
/// <param name="Count">The counter now (0 after a reset the character has not seen).</param>
/// <param name="Target">The count of the step.</param>
/// <param name="Minutes">Estimated minutes of play for the step (0 for a step that runs along with others).</param>
/// <param name="Passive">The step runs along with other play (time spent in the Abyss): its minutes are not added.</param>
/// <param name="When">When the step becomes possible (a boss comes back, the artifact siege is on); null = now.</param>
/// <param name="WhenIsDay">Only the day is known (not used for the siege any more: its table time of day, 21:00, is the players' local time, confirmed by the project lead for German players).</param>
/// <param name="Why">For a blocked step: "late" (comes after the reset), "unknown" (the game's list says nothing), "rare" (too few bosses before the reset).</param>
/// <param name="StandAfter">The week's points after this step, when the plan is followed in order.</param>
/// <param name="Steps">How many steps of the mission the line holds (the plan joins the steps of a mission it takes one after the other; <c>Step</c> is the last of them, the points and minutes are the sum).</param>
public sealed record PlanRow(WeeklyMission Mission, MissionStep Step, int Count, int Target, int Points, double Minutes, bool Passive, PlanState State, DateTime? When, bool WhenIsDay, string Why, int StandAfter, int Steps = 1);

/// <summary>The plan for one character: what to collect, what to play in what order, what cannot be done before the weekly reset.</summary>
public sealed record WeekPlan(bool HasData, int Claimed, int Waiting, int OpenPoints, DateTime Reset, int BudgetMinutes, double PlannedMinutes, int PlannedPoints, IReadOnlyList<PlanRow> Rows)
{
    /// <summary>The week's points after collecting what waits and playing the plan.</summary>
    public int Final => Claimed + Waiting + PlannedPoints;

    public static readonly IReadOnlyList<int> Thresholds = new[] { 1500, 3000, 4500, 6000, 8000 };

    public static int? NextThreshold(int points) => Thresholds.Where(t => t > points).Select(t => (int?)t).FirstOrDefault();
}

/// <summary>
/// The week plan of the season missions: the points of the weekly reward (steps 1,500 / 3,000 / 4,500 / 6,000 / 8,000) come from collecting the rewards of the weekly missions'
/// steps, and the week ends on Wednesday 16:00 game time (<see cref="ChecklistManual.LastWeeklyReset"/>). With the counters of a character and the time the player has, the plan picks the
/// steps with the most points per minute. A boss mission counts only when the game's own boss list says the boss comes before the reset (<see cref="Aion2FieldBosses.ServerStatusOf"/>),
/// an artifact siege mission only when the siege is on a day before it. The minutes are estimates of ours, not game data.
/// </summary>
public static class WeekPlanner
{
    /// <summary>The estimated minutes of play per unit of a mission's counter, by the game's objective type (the tab where the type means different things).</summary>
    public static (double PerUnit, bool Passive) Cost(WeeklyMission mission) => mission.Kind switch
    {
        "ElapsedTime" => (1, true),
        "KillPC" => (4, false),
        "KillNpcAll" => (0.09, false),
        "KillNpc" => (8, false),
        "AbyssArtifactEventComplete" or "AbyssArtifactEventWin" => (30, false),
        "ArenaWin" => (8, false),
        "ClearDungeon" => (mission.Tab == "Arena" ? 6 : mission.Tab == "PartyChallenge" ? 20 : 25, false),
        "ClearDungeonLimitDeath" => (22, false),
        "ClearAchievement" => (20, false),
        "ClearBossChallenge" => (8, false),
        "GetItem" => (0.03, false),
        "KillPCAbyssGrade" => (5, false),
        "GatherCategorySuccess" => (1.5, false),
        "ClearQuest" => (6, false),
        _ => (10, false),
    };

    private sealed record Item(WeeklyMission Mission, MissionStep Step, int Count, double Minutes, bool Passive, bool Blocked, DateTime? When, bool WhenIsDay, string Why);

    /// <summary>The plan for a character at <paramref name="now"/> (local time) with <paramref name="budgetMinutes"/> minutes of play (0 or less: no limit).</summary>
    public static WeekPlan Build(TicketCharacter character, DateTime now, int budgetMinutes, Func<int, BossStatus?> boss)
    {
        DateTime lastReset = ChecklistManual.LastWeeklyReset(now);
        DateTime reset = lastReset.AddDays(7);
        long lastResetMs = new DateTimeOffset(lastReset).ToUnixTimeMilliseconds();
        List<WeeklyMission> mine = Aion2Missions.Missions.Where(m => character.Tickets.ContainsKey(m.Id)).ToList();
        if (mine.Count == 0)
        {
            return new WeekPlan(false, 0, 0, 0, reset, budgetMinutes, 0, 0, Array.Empty<PlanRow>());
        }

        var waitingRows = new List<PlanRow>();
        var chains = new List<List<Item>>();
        int claimedFromLevels = 0, waiting = 0, open = 0;
        foreach (WeeklyMission mission in mine)
        {
            TicketValue t = character.Tickets[mission.Id];
            IReadOnlyList<MissionStep> steps = mission.Steps;
            int level = Math.Clamp(t.Bonus, 0, steps.Count);
            int raw = t.Count;
            bool stale = t.TimeMs > 0 && t.TimeMs < lastResetMs;
            int readyTo = level;
            while (readyTo < steps.Count && raw >= steps[readyTo].Count)
            {
                readyTo++;
            }

            if (stale && readyTo == level)
            {
                // a week the character has not seen end: nothing of it counts any more (a finished step that was never collected does; the game keeps it)
                level = 0;
                readyTo = 0;
            }

            int cum = stale ? 0 : raw;
            if (!stale)
            {
                for (int i = 0; i < level; i++)
                {
                    claimedFromLevels += steps[i].Points;
                }
            }

            for (int i = level; i < readyTo; i++)
            {
                waiting += steps[i].Points;
                waitingRows.Add(new PlanRow(mission, steps[i], raw, steps[i].Count, steps[i].Points, 0, false, PlanState.Waiting, null, false, "", 0));
            }

            (double perUnit, bool passive) = Cost(mission);
            var chain = new List<Item>();
            int previous = cum;
            bool blocked = false;
            for (int i = readyTo; i < steps.Count; i++)
            {
                MissionStep step = steps[i];
                open += step.Points;
                int need = Math.Max(0, step.Count - cum);
                double units = Math.Max(0, step.Count - previous);
                previous = step.Count;
                (bool ok, DateTime? when, bool dayOnly, string why) = blocked ? (false, null, false, "") : Gate(mission, need, now, reset, boss);
                if (!ok)
                {
                    blocked = true;
                }

                chain.Add(new Item(mission, step, Math.Min(cum, step.Count), units * perUnit, passive, !ok, when, dayOnly, why));
            }

            chains.Add(chain);
        }

        int claimed;
        if (character.Tickets.TryGetValue(Aion2Tickets.WeeklyPointsId, out TicketValue points))
        {
            claimed = points.TimeMs >= lastResetMs ? Math.Min(points.Count, Aion2Tickets.WeeklyPointsMax) : 0;
        }
        else
        {
            claimed = Math.Min(claimedFromLevels, Aion2Tickets.WeeklyPointsMax);
        }

        // the plan: the step with the most points per minute that still fits, until nothing does (steps of a mission in their order)
        double budget = budgetMinutes > 0 ? budgetMinutes : double.MaxValue;
        double used = 0;
        var chosen = new List<Item>();
        var next = chains.Select(_ => 0).ToArray();
        while (true)
        {
            int bestChain = -1;
            double bestRatio = -1;
            for (int c = 0; c < chains.Count; c++)
            {
                if (next[c] >= chains[c].Count || chains[c][next[c]].Blocked)
                {
                    continue;
                }

                Item item = chains[c][next[c]];
                double cost = item.Passive ? 0 : item.Minutes;
                if (used + cost > budget || (item.Passive && item.Minutes > budget))
                {
                    continue;
                }

                double ratio = item.Step.Points / Math.Max(cost, 0.5);
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    bestChain = c;
                }
            }

            if (bestChain < 0)
            {
                break;
            }

            Item picked = chains[bestChain][next[bestChain]++];
            chosen.Add(picked);
            used += picked.Passive ? 0 : picked.Minutes;
        }

        // in the order it works: what can be done now first (as chosen), then what waits for a boss or a siege, by time
        List<Item> ordered = chosen.Where(i => i.When is null).Concat(chosen.Where(i => i.When is not null).OrderBy(i => i.When)).ToList();
        var rows = new List<PlanRow>(waitingRows.OrderByDescending(r => r.Points));
        int stand = claimed + waiting;
        // the steps of one mission the plan takes become one line (ten steps of "kill officers" are one task of 20 kills)
        var lines = new List<(Item First, Item Last, int Points, double Minutes, int Steps)>();
        foreach (Item i in ordered)
        {
            int at = lines.FindIndex(l => l.Last.Mission.Id == i.Mission.Id);
            double minutes = i.Passive ? 0 : i.Minutes;
            if (at >= 0)
            {
                (Item first, _, int linePoints, double lineMinutes, int lineSteps) = lines[at];
                lines[at] = (first, i, linePoints + i.Step.Points, lineMinutes + minutes, lineSteps + 1);
            }
            else
            {
                lines.Add((i, i, i.Step.Points, minutes, 1));
            }
        }

        foreach ((Item first, Item last, int linePoints, double lineMinutes, int lineSteps) in lines)
        {
            stand += linePoints;
            rows.Add(new PlanRow(first.Mission, last.Step, first.Count, last.Step.Count, linePoints, lineMinutes, first.Passive, PlanState.Plan, first.When, first.WhenIsDay, "", stand, lineSteps));
        }

        // what is possible but no longer fits (the first step of each mission) and what cannot be done before the reset (the first step)
        for (int c = 0; c < chains.Count; c++)
        {
            if (next[c] < chains[c].Count)
            {
                Item item = chains[c][next[c]];
                rows.Add(new PlanRow(item.Mission, item.Step, item.Count, item.Step.Count, item.Step.Points, item.Passive ? 0 : item.Minutes, item.Passive, item.Blocked ? PlanState.Blocked : PlanState.Spare, item.When, item.WhenIsDay, item.Why, 0));
            }
        }

        return new WeekPlan(true, claimed, waiting, open, reset, budgetMinutes, used, ordered.Sum(i => i.Step.Points), rows);
    }

    /// <summary>Whether a step that needs <paramref name="need"/> more of the counter can be done before the reset: the bosses the game's list gives times for, the siege's days.</summary>
    private static (bool Ok, DateTime? When, bool DayOnly, string Why) Gate(WeeklyMission mission, int need, DateTime now, DateTime reset, Func<int, BossStatus?> boss)
    {
        need = Math.Max(1, need);
        if (mission.Kind == "KillNpc" && mission.Npcs.Count > 0)
        {
            var listed = mission.Npcs.Select(n => boss(n)).Where(s => s is not null).Select(s => s!).ToList();
            if (listed.Count == 0)
            {
                return (true, null, false, "");
            }

            var times = new List<DateTime>();
            DateTime? late = null;
            int unknown = 0;
            foreach (BossStatus s in listed)
            {
                if (s.Phase is BossPhase.Unknown or BossPhase.Fixed)
                {
                    unknown++;
                }
                else if (s.Phase == BossPhase.Now)
                {
                    times.Add(now);
                }
                else if (s.Phase == BossPhase.Respawn && s.At is { } at)
                {
                    if (at < reset)
                    {
                        times.Add(at);
                    }
                    else if (late is null || at < late)
                    {
                        late = at;
                    }
                }
            }

            times.Sort();
            if (times.Count >= need)
            {
                DateTime when = times[need - 1];
                return (true, when <= now.AddMinutes(1) ? null : when, false, "");
            }

            if (times.Count > 0)
            {
                return (false, null, false, "rare");
            }

            // a target the list says nothing about may come in time: only when every target is known to come late is the step out of reach
            return unknown == 0 && late is { } l ? (false, l, false, "late") : (false, null, false, "unknown");
        }

        if (mission.Event is { } key && Aion2Missions.EventOf(key) is { } ev && ev.Days.Count > 0)
        {
            var occurrences = new List<DateTime>();
            DateTime? after = null;
            for (int d = 0; d <= 14; d++)
            {
                DateTime day = now.Date.AddDays(d);
                if (!ev.Days.Contains((int)day.DayOfWeek))
                {
                    continue;
                }

                DateTime occurrence = day + TimeSpan.FromHours(ev.At.Count > 0 ? ev.At[0] : 0);
                if (occurrence.AddMinutes(60) <= now)
                {
                    continue;
                }

                if (occurrence < reset)
                {
                    occurrences.Add(occurrence);
                }
                else
                {
                    after ??= occurrence;
                }
            }

            if (occurrences.Count >= need)
            {
                DateTime when = occurrences[need - 1];
                return (true, when <= now ? null : when, false, "");
            }

            return occurrences.Count > 0 ? (false, null, false, "rare") : (false, after, false, "late");
        }

        return (true, null, false, "");
    }
}
