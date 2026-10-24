namespace AionDPS.Combat;

/// <summary>A player's death: when their hit points reached 0, and the hit that did it, if one
/// landed in the seconds before, and when they were revived (null: not within what was looked at).</summary>
public readonly record struct Death(DateTime At, int PlayerId, DamageEvent? KillingBlow, DateTime? RevivedAt = null)
{
    /// <summary>How long the player was dead: until the revive, else until <paramref name="end"/> (the end of what is looked at).</summary>
    public TimeSpan DeadFor(DateTime end) => TimeSpan.FromTicks(Math.Max(0, ((RevivedAt ?? end) - At).Ticks));
}

/// <summary>
/// Deaths read from a player's own hit points: the server reports every party member's, and a
/// death is a reading at 0 after one above it (a revive brings them back up, the next 0 is another
/// death). Verified on the solo Krao Cave wipe (2026-10-02 12:49:20, Ultimate Berk's 345 "Attack")
/// and a Draupnir run (Butterfinger twice, Aurulio once). The killing blow is the last hit taken in
/// the <see cref="BlowWindow"/> before; hits a player deals to themselves (a Sorcerer's Absorb
/// Essence costs hit points) are none - the caller passes hostile hits only.
/// </summary>
public static class Deaths
{
    public static readonly TimeSpan BlowWindow = TimeSpan.FromSeconds(3);

    /// <summary>A death frame comes about 50 ms after the hit points reach 0; one within this many seconds is that death's.</summary>
    public static readonly TimeSpan CreditWindow = TimeSpan.FromSeconds(1);

    /// <param name="readings">The player's hit points, oldest first.</param>
    /// <param name="hitsTaken">Hostile hits on the player, any order.</param>
    /// <param name="credits">The skills the player's death frames named (see <see cref="AionDPS.Aion2.KillCredit"/>), when known: the killing blow is then the
    /// latest hit with that skill, which is right where the last hit of the seconds before is not (in a recording 797 of 1,166 deaths named the last hit).</param>
    public static List<Death> Find(int playerId, IReadOnlyList<(DateTime At, long Hp)> readings, IReadOnlyList<DamageEvent> hitsTaken, IReadOnlyList<AionDPS.Aion2.KillCredit>? credits = null)
    {
        var deaths = new List<Death>();
        long? previous = null;
        foreach ((DateTime at, long hp) in readings)
        {
            if (hp > 0 && previous == 0 && deaths.Count > 0 && deaths[^1].RevivedAt is null)
            {
                deaths[^1] = deaths[^1] with { RevivedAt = at }; // back on their feet
            }

            if (hp == 0 && previous is not 0)
            {
                DamageEvent? blow = null;
                int named = credits?.Where(c => c.At >= at - TimeSpan.FromMilliseconds(200) && c.At <= at + CreditWindow).Select(c => c.SkillId).FirstOrDefault() ?? 0;
                foreach (DamageEvent hit in hitsTaken)
                {
                    if (hit.Timestamp <= at && hit.Timestamp >= at - BlowWindow && (blow is null || hit.Timestamp >= blow.Value.Timestamp)
                        && (named == 0 || hit.SkillId == named))
                    {
                        blow = hit;
                    }
                }

                if (blow is null && named != 0)
                {
                    // no hit with the named skill was counted (a skill the meter does not read): the last hit, as before
                    foreach (DamageEvent hit in hitsTaken)
                    {
                        if (hit.Timestamp <= at && hit.Timestamp >= at - BlowWindow && (blow is null || hit.Timestamp >= blow.Value.Timestamp))
                        {
                            blow = hit;
                        }
                    }
                }

                deaths.Add(new Death(at, playerId, blow));
            }

            previous = hp;
        }

        return deaths;
    }
}
