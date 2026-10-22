namespace AionDPS.Aion2.Protocol;

/// <summary>
/// A boss's real 100 % for the overlay: the spawn frame announces the maximum (see Aion2FrameDecoder.ReadSpawnHitPoints), so a meter that
/// first sees the boss at 40 % still draws the bar at 40 %. An exploration boss ("party scaled") has the hit points of its spawn
/// value times the group size in the HP frames, the spawn frame carrying the value for one player.
/// </summary>
public static class Aion2BossHp
{
    /// <summary>The most players an exploration group holds.</summary>
    private const int MaxGroup = 5;

    /// <summary>
    /// The boss's real maximum. Base = spawn maximum, else the table's; with neither, the old rule (reported maximum or highest reading).
    /// A party-scaled boss: base times the larger of the group size and the multiple the readings already show. Otherwise the larger of
    /// base and highest reading (a buff makes Talisra and Atiel grow after the spawn). Never below the highest reading or the maximum the server reported.
    /// </summary>
    public static long Compute(long? spawnMax, long catalogMax, bool partyScaled, int groupSize, long highestSeen, long reportedOrHighest)
    {
        long baseHp = spawnMax is > 0 ? spawnMax.Value : catalogMax;
        long real;
        if (baseHp <= 0)
        {
            real = reportedOrHighest;
        }
        else if (partyScaled)
        {
            long seen = (long)Math.Round(highestSeen / (double)baseHp);
            long k = Math.Clamp(Math.Max(groupSize, seen), 1, MaxGroup);
            real = baseHp * k;
        }
        else
        {
            real = baseHp;
        }

        return Math.Max(real, Math.Max(highestSeen, reportedOrHighest));
    }

    /// <summary>The boss's real maximum for an entity; null when no hit points of it are known at all.</summary>
    public static long? RealMax(Aion2EntityDirectory dir, int entityId)
    {
        var info = dir.BossNpcIdOf(entityId) is int npc ? Aion2BossCatalog.Find(npc) : null;
        long highest = dir.HitPoints.HighestSeen(entityId) ?? 0;
        long reported = Math.Max(dir.HitPoints.MaxOf(entityId) ?? 0, highest);
        long max = Compute(dir.HitPoints.SpawnMaxOf(entityId), info?.MaxHp ?? 0, info?.PartyScaled == true, Math.Max(dir.PartySize, 1), highest, reported);
        return max > 0 ? max : null;
    }
}
