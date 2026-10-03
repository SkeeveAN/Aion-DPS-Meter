namespace AionDPS.Combat;

/// <summary>A player's death: when their hit points reached 0, and the hit that did it, if one
/// landed in the seconds before.</summary>
public readonly record struct Death(DateTime At, int PlayerId, DamageEvent? KillingBlow);

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

    /// <param name="readings">The player's hit points, oldest first.</param>
    /// <param name="hitsTaken">Hostile hits on the player, any order.</param>
    public static List<Death> Find(int playerId, IReadOnlyList<(DateTime At, long Hp)> readings, IReadOnlyList<DamageEvent> hitsTaken)
    {
        var deaths = new List<Death>();
        long? previous = null;
        foreach ((DateTime at, long hp) in readings)
        {
            if (hp == 0 && previous is not 0)
            {
                DamageEvent? blow = null;
                foreach (DamageEvent hit in hitsTaken)
                {
                    if (hit.Timestamp <= at && hit.Timestamp >= at - BlowWindow && (blow is null || hit.Timestamp >= blow.Value.Timestamp))
                    {
                        blow = hit;
                    }
                }

                deaths.Add(new Death(at, playerId, blow));
            }

            previous = hp;
        }

        return deaths;
    }
}
