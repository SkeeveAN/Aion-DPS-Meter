using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>
/// What the upload adds to a player's time axis for the website's analysis of a fight: his damage on the boss in 5-second slices (the group's damage curve is their sum),
/// his damage on everything else, the time he was dead, and the hostile damage he took by source and skill. All of it is read from hits and deaths the stream named; nothing is
/// guessed. Pure, so a test can run it.
/// </summary>
public static class FightAnalysis
{
    /// <summary>The length of one damage slice in tenths of a second (the backend and the website read it as 5 seconds).</summary>
    public const int BucketTenths = 50;

    /// <summary>The most slices one player carries (the backend takes 720: one hour).</summary>
    public const int MaxBuckets = 720;

    /// <summary>The most dead spans (start, end pairs) one player carries (the backend takes 80 numbers).</summary>
    public const int MaxDeadSpans = 40;

    /// <summary>The most damage-taken lines one player carries (the backend takes 16).</summary>
    public const int MaxTakenLines = 16;

    private sealed class Taken
    {
        public int Hits;
        public long Total;
        public long Max;
    }

    /// <summary>
    /// The analysis of every player in <paramref name="players"/> over the window.
    /// </summary>
    /// <param name="events">Every damage and heal event of the window (heals and self-inflicted damage are ignored here).</param>
    /// <param name="bossId">The fight's target.</param>
    /// <param name="isMonster">Whether an entity is a monster the server announced (an add when it is not the boss).</param>
    public static Dictionary<int, AnalysisUpload> Build(IReadOnlyList<DamageEvent> events, int bossId, FightExtras extras, IReadOnlyCollection<int> players, Func<int, bool> isMonster, DateTime start, DateTime end)
    {
        var wanted = players.ToHashSet();
        var damage = new Dictionary<int, long[]>();
        var addDamage = new Dictionary<int, long>();
        var taken = new Dictionary<int, Dictionary<(string From, string Skill), Taken>>();
        int buckets = (int)Math.Min(MaxBuckets, Math.Max(1, Math.Ceiling((end - start).TotalSeconds * 10 / BucketTenths)));

        foreach (DamageEvent e in events)
        {
            if (e.IsHeal || e.Estimated || e.Amount <= 0 || e.SourceObjectId == e.TargetObjectId || e.Timestamp < start || e.Timestamp > end)
            {
                continue;
            }

            if (wanted.Contains(e.SourceObjectId))
            {
                if (e.TargetObjectId == bossId)
                {
                    if (!damage.TryGetValue(e.SourceObjectId, out long[]? slices))
                    {
                        damage[e.SourceObjectId] = slices = new long[buckets];
                    }

                    int at = (int)((e.Timestamp - start).TotalSeconds * 10 / BucketTenths);
                    if (at >= 0 && at < buckets)
                    {
                        slices[at] += e.Amount;
                    }
                }
                else if (isMonster(e.TargetObjectId))
                {
                    addDamage[e.SourceObjectId] = addDamage.GetValueOrDefault(e.SourceObjectId) + e.Amount;
                }
            }

            if (wanted.Contains(e.TargetObjectId) && !wanted.Contains(e.SourceObjectId))
            {
                string from = e.SourceObjectId == bossId ? "boss" : isMonster(e.SourceObjectId) ? "add" : "other";
                string skill = string.IsNullOrEmpty(e.Skill) ? "?" : e.Skill!;
                if (!taken.TryGetValue(e.TargetObjectId, out var lines))
                {
                    taken[e.TargetObjectId] = lines = new Dictionary<(string, string), Taken>();
                }

                if (!lines.TryGetValue((from, skill), out Taken? line))
                {
                    lines[(from, skill)] = line = new Taken();
                }

                line.Hits += Math.Max(1, e.HitCount);
                line.Total += e.Amount;
                line.Max = Math.Max(line.Max, e.Amount);
            }
        }

        var result = new Dictionary<int, AnalysisUpload>();
        foreach (int player in wanted)
        {
            IReadOnlyList<int> dead = DeadSpans(extras, player, start, end);
            var takenLines = (taken.GetValueOrDefault(player) ?? new())
                .OrderByDescending(kv => kv.Value.Total)
                .Take(MaxTakenLines)
                .Select(kv => new TakenUpload(kv.Key.From, kv.Key.Skill.Length > 120 ? kv.Key.Skill[..120] : kv.Key.Skill, (int)Math.Min(kv.Value.Hits, 100_000), kv.Value.Total, kv.Value.Max))
                .ToList();
            long[]? slices = damage.GetValueOrDefault(player);
            long add = addDamage.GetValueOrDefault(player);
            if (slices is null && add == 0 && dead.Count == 0 && takenLines.Count == 0)
            {
                continue;
            }

            result[player] = new AnalysisUpload(slices ?? new long[buckets], add, dead, takenLines);
        }

        return result;
    }

    /// <summary>The spans the player was dead in the window as flattened (start, end) pairs in tenths of a second: from the death to the revive, else to the end of the window.</summary>
    public static IReadOnlyList<int> DeadSpans(FightExtras extras, int playerId, DateTime start, DateTime end)
    {
        var spans = new List<int>();
        foreach (DeathRecord death in (extras.Deaths ?? Array.Empty<DeathRecord>()).Where(d => d.Entity == playerId).OrderBy(d => d.At))
        {
            DateTime to = death.RevivedAt ?? end;
            if (to < start || death.At > end)
            {
                continue;
            }

            DateTime from = death.At < start ? start : death.At;
            if (to > end)
            {
                to = end;
            }

            int a = Tenths((from - start).TotalSeconds);
            int b = Tenths((to - start).TotalSeconds);
            if (b > a && spans.Count < MaxDeadSpans * 2)
            {
                spans.Add(a);
                spans.Add(b);
            }
        }

        return spans;
    }

    private static int Tenths(double seconds) => (int)Math.Clamp(Math.Round(seconds * 10), 0, 100_000);
}
