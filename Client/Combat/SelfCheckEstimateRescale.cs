namespace AionDPS.Combat;

/// <summary>The final correction of a world boss's estimated damage: the sum lands on the boss's hit points, too much and too little alike, and exact hits stay.</summary>
public static class SelfCheckEstimateRescale
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] World boss estimate, final correction:");
        DateTime t0 = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        List<DamageEvent> Hits(long estimatedEach) =>
            Enumerable.Range(0, 100).Select(i => new DamageEvent(t0.AddMilliseconds(i * 100), 1 + i % 10, 99, estimatedEach, false, Estimated: true))
                .Append(new DamageEvent(t0, 500, 99, 1_000_000, false)).ToList();

        // too much (115 %): 100 hits of 1.2 M + 1 M exact against 100 M
        List<DamageEvent> over = Hits(1_200_000);
        double fOver = EstimateRescale.FactorFor(over, 100_000_000) ?? 0;
        List<DamageEvent> overFixed = EstimateRescale.Apply(over, fOver);
        bool lands = Math.Abs(overFixed.Sum(e => e.Amount) - 100_000_000) < 200 && fOver < 1 && overFixed.Count(e => !e.Estimated && e.Amount == 1_000_000) == 1;
        // too little (83 %)
        double fUnder = EstimateRescale.FactorFor(Hits(800_000), 100_000_000) ?? 0;
        bool under = fUnder > 1 && Math.Abs(EstimateRescale.Apply(Hits(800_000), fUnder).Sum(e => e.Amount) - 100_000_000) < 200;
        // too few estimates, nothing lost beyond the exact damage, a wild reading
        bool guards = EstimateRescale.FactorFor(over.Take(10).ToList(), 100_000_000) is null
            && EstimateRescale.FactorFor(over, 500_000) is null
            && EstimateRescale.FactorFor(over, 100_000_000_000) is double wild && wild == EstimateRescale.MaxFactor;
        Console.WriteLine($"  -> an overshoot is scaled down onto the hit points, exact hits stay: {lands}");
        Console.WriteLine($"  -> a shortfall is scaled up onto them: {under}");
        Console.WriteLine($"  -> few estimates or nothing left to explain change nothing, a wild reading is bounded: {guards}");
        return lands && under && guards;
    }
}
