namespace AionDPS.Combat;

/// <summary>
/// The running totals of <see cref="DamageIndex"/> against counting the whole event list by hand, in several steps - including targets that
/// only become known as players after they were hit (their earlier hits then count for PvP, exactly as a rescan counts them).
/// </summary>
public static class SelfCheckDamageIndex
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Damage index against a hand count:");
        var rng = new Random(20261018);
        var start = new DateTime(2026, 10, 18, 20, 0, 0);
        var aggregator = new LiveAggregator();
        var index = new DamageIndex();
        var players = new HashSet<int>();
        const int sources = 12, targets = 40;
        var all = new List<DamageEvent>();
        bool totalsOk = true, pvpOk = true, rangeOk = true, targetsOk = true;
        int checks = 0;

        for (int step = 0; step < 8; step++)
        {
            var batch = new List<DamageEvent>();
            for (int i = 0; i < 400; i++)
            {
                DateTime at = start.AddSeconds(step * 60 + rng.NextDouble() * 60);
                bool heal = rng.Next(12) == 0;
                batch.Add(new DamageEvent(at, 1000 + rng.Next(sources), 5000 + rng.Next(targets), 100 + rng.Next(5000), heal, Estimated: !heal && rng.Next(5) == 0));
            }

            batch.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            aggregator.IngestEvents(batch);
            all.AddRange(batch);

            // from step 2 on, a few more targets turn out to be players (the directory learns it late)
            if (step >= 2)
            {
                for (int k = 0; k < 3; k++)
                {
                    players.Add(5000 + rng.Next(targets));
                }
            }

            index.Update(aggregator, id => players.Contains(id));

            var damage = all.Where(e => !e.IsHeal).ToList();
            foreach (IGrouping<int, DamageEvent> group in damage.GroupBy(e => e.SourceObjectId))
            {
                DamageIndex.Stats? stats = index.BySource.GetValueOrDefault(group.Key);
                totalsOk &= stats is not null && stats.Sum == group.Sum(e => e.Amount) && stats.EstimatedSum == group.Where(e => e.Estimated).Sum(e => e.Amount)
                    && stats.First == group.Min(e => e.Timestamp) && stats.Last == group.Max(e => e.Timestamp);
            }

            totalsOk &= index.BySource.Count == damage.Select(e => e.SourceObjectId).Distinct().Count() && index.DamageCount == damage.Count
                && index.DamageFirst == damage.Min(e => e.Timestamp) && index.DamageLast == damage.Max(e => e.Timestamp) && index.Heals.Count == all.Count - damage.Count;
            targetsOk &= index.Targets.SetEquals(damage.Select(e => e.TargetObjectId)) && damage.GroupBy(e => e.TargetObjectId).All(g => index.TakenByTarget.GetValueOrDefault(g.Key) == g.Sum(e => e.Amount));

            var pvp = damage.Where(e => players.Contains(e.TargetObjectId)).ToList();
            pvpOk &= index.PvpCount == pvp.Count && index.PvpBySource.Count == pvp.Select(e => e.SourceObjectId).Distinct().Count();
            foreach (IGrouping<int, DamageEvent> group in pvp.GroupBy(e => e.SourceObjectId))
            {
                DamageIndex.Stats? stats = index.PvpBySource.GetValueOrDefault(group.Key);
                pvpOk &= stats is not null && stats.Sum == group.Sum(e => e.Amount) && stats.EstimatedSum == group.Where(e => e.Estimated).Sum(e => e.Amount)
                    && stats.First == group.Min(e => e.Timestamp) && stats.Last == group.Max(e => e.Timestamp);
            }

            if (pvp.Count > 0)
            {
                pvpOk &= index.PvpFirst == pvp.Min(e => e.Timestamp) && index.PvpLast == pvp.Max(e => e.Timestamp);
            }

            foreach (int target in damage.Select(e => e.TargetObjectId).Distinct().Take(10))
            {
                DateTime from = start.AddSeconds(rng.Next(0, step * 60 + 30)), to = from.AddSeconds(rng.Next(1, 120));
                rangeOk &= index.TakenBetween(target, from, to) == damage.Where(e => e.TargetObjectId == target && e.Timestamp >= from && e.Timestamp <= to).Sum(e => e.Amount);
            }

            checks++;
        }

        bool ok = totalsOk && pvpOk && rangeOk && targetsOk;
        Console.WriteLine($"  -> {checks} steps: totals per source={totalsOk}, targets and damage taken={targetsOk}, PvP incl. late players={pvpOk}, damage taken between two times={rangeOk}: {(ok ? "OK" : "FAILED")}");
        return ok;
    }
}
