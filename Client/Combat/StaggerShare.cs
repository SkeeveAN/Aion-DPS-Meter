namespace AionDPS.Combat;

/// <summary>Who lowered a boss's stagger bar and by how much: <see cref="BySource"/> is the drop credited to each attacker, <see cref="Assigned"/> their sum, <see cref="Total"/> all the bar fell.</summary>
public sealed record StaggerShareResult(IReadOnlyDictionary<int, int> BySource, int Assigned, int Total)
{
    public static StaggerShareResult Empty { get; } = new(new Dictionary<int, int>(), 0, 0);
}

/// <summary>
/// The share of each player in a boss's stagger. The game sends the bar's reading after every hit that counted (not a total), in the order of
/// the hits and in the same packet: a reading of 7 beside a hit of 7 names who did it. Every hit has a fixed drop (<see cref="Aion2.Aion2StaggerTable"/>;
/// a buff of the attacker raises it by half, rounded down), and not every hit counts (the bar does not fall while it is recharging after a break),
/// so adding up the hits would credit about 1.7 times too much. Instead each reading that fell is matched, in order, to the next hit of its packet
/// whose drop it equals; a drop no hit explains stays unassigned (<see cref="StaggerShareResult.Total"/> minus <see cref="StaggerShareResult.Assigned"/>)
/// rather than being guessed. A fall of a fifth of the bar or more that no hit explains is the boss's scripted hit on itself at the start of a fight and does not count.
/// Measured on five recordings, see the project notes.
/// </summary>
/// <summary>A fall of the bar no hit explained: the drop, and the hits that stood in its packet (skill, the table's value or 0 for an unknown skill, attacker).</summary>
public sealed record StaggerMiss(DateTime At, int Drop, IReadOnlyList<(int Skill, int Value, int Source)> Candidates);

public static class StaggerShare
{
    /// <summary>Readings and hits of one packet carry (nearly) the same time.</summary>
    private static readonly TimeSpan Packet = TimeSpan.FromMilliseconds(12);

    private static readonly TimeSpan LongestGap = TimeSpan.FromSeconds(5);

    public static StaggerShareResult Compute(IReadOnlyList<DamageEvent> events, IReadOnlyList<StaggerEvent> staggers, int target, DateTime from, DateTime to, Func<int, int> dropOf, ICollection<StaggerMiss>? misses = null)
    {
        var readings = staggers.Where(s => s.Entity == target && s.At >= from - Packet && s.At <= to + Packet).OrderBy(s => s.At).ToList();
        if (readings.Count < 2)
        {
            return StaggerShareResult.Empty;
        }

        var hits = events.Where(e => e.TargetObjectId == target && !e.IsHeal && !e.IsTick && e.Amount > 0 && e.SkillId != 0)
            .OrderBy(e => e.Timestamp).ToList();
        var times = hits.Select(h => h.Timestamp).ToArray();
        var used = new bool[hits.Count];
        var bySource = new Dictionary<int, int>();
        int assigned = 0, total = 0;

        StaggerEvent? previous = null;
        for (int i = 0; i < readings.Count;)
        {
            // the readings of one packet
            int last = i;
            while (last + 1 < readings.Count && readings[last + 1].At - readings[i].At <= Packet)
            {
                last++;
            }

            int first = FirstAtOrAfter(times, readings[i].At - Packet);
            int hitIndex = first;
            for (int r = i; r <= last; r++)
            {
                StaggerEvent reading = readings[r];
                if (reading.Broke)
                {
                    previous = null;
                    continue;
                }

                int drop = previous is { } p && p.Current > 0 && reading.Current >= 0 && reading.Current < p.Current && reading.At - p.At <= LongestGap
                    ? p.Current - reading.Current : 0;
                previous = reading;
                if (drop <= 0)
                {
                    continue;
                }

                total += drop;
                bool matched = false;
                for (int h = hitIndex; h < hits.Count && times[h] <= readings[last].At + Packet; h++)
                {
                    if (used[h])
                    {
                        continue;
                    }

                    int value = dropOf(hits[h].SkillId);
                    if (value > 0 && (drop == value || drop == value * 3 / 2))
                    {
                        used[h] = true;
                        hitIndex = h + 1;
                        assigned += drop;
                        bySource[hits[h].SourceObjectId] = bySource.GetValueOrDefault(hits[h].SourceObjectId) + drop;
                        matched = true;
                        break;
                    }
                }

                if (!matched && reading.Max > 0 && drop * 5 >= reading.Max)
                {
                    // A fall of a fifth of the bar or more with no hit behind it is the boss's own start-of-fight hit (skill 1000007, 500 million on itself,
                    // seen 27 times: the bar goes 1,200 to 840, or 480 to 240): no player's, so it is not part of the bar's fall that is shared out.
                    total -= drop;
                    continue;
                }

                if (!matched && misses is not null)
                {
                    var candidates = new List<(int, int, int)>();
                    for (int h = first; h < hits.Count && times[h] <= readings[last].At + Packet; h++)
                    {
                        candidates.Add((hits[h].SkillId, dropOf(hits[h].SkillId), hits[h].SourceObjectId));
                    }

                    misses.Add(new StaggerMiss(reading.At, drop, candidates));
                }
            }

            i = last + 1;
        }

        return new StaggerShareResult(bySource, assigned, total);
    }

    private static int FirstAtOrAfter(DateTime[] times, DateTime at)
    {
        int lo = 0, hi = times.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (times[mid] < at)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
