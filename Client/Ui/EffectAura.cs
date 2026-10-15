namespace AionDPS.Ui;

/// <summary>
/// Tells an aura from an ordinary timed effect. A Chanter's mantra lasts 2.4 s and is renewed about every second for as long as the Chanter keeps it up (recordings of
/// 2026-10-14: one change frame per second, same instance, the end moving 1 s forward each time), so its time left jumps between 1.4 and 2.4 s although the effect is
/// in effect permanent. An effect whose end moved forward while it was still running, twice, and whose length is short, is an aura: shown as endless and sorted last.
/// </summary>
public static class EffectAura
{
    /// <summary>Only an effect of this length or shorter can be an aura (the mantras run 2.4 s).</summary>
    public const int MaxDurationMs = 6000;

    public const int RenewalsNeeded = 2;

    /// <summary>The end must move at least this far to count as a renewal (the frames of one renewal differ by about a second).</summary>
    private const int MinStepMs = 300;

    /// <summary>The renewal count and aura flag after seeing the effect with this end now; <paramref name="previousEnd"/> is the end seen before.</summary>
    public static (int Renewals, bool Aura) Next(DateTime? previousEnd, DateTime? end, DateTime now, int durationMs, int renewals, bool aura)
    {
        if (previousEnd is { } before && end is { } after && before > now && after > before.AddMilliseconds(MinStepMs))
        {
            renewals++;
            if (renewals >= RenewalsNeeded && durationMs is > 0 and <= MaxDurationMs)
            {
                aura = true;
            }
        }

        return (renewals, aura);
    }
}
