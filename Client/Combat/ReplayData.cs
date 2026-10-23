namespace AionDPS.Combat;

public enum ReplayRole
{
    Player,
    Boss,

    /// <summary>Another monster the group fought (an add): a small marker that moves, without areas or health.</summary>
    Add,
}

/// <summary>Someone shown on the replay map, with where they were.</summary>
public sealed record ReplayEntity(int Id, string Name, string ClassName, ReplayRole Role, PositionTrack Track, bool IsSelf = false)
{
    /// <summary>
    /// The own character's place is only reported now and then (with skills and corrections; the server sends no running position of one's own character), so between two
    /// readings the replay draws a straight walk: an estimate. True when no reading lies within <see cref="ExactSeconds"/> of this moment; false for everybody else, whose
    /// movement frames are readings.
    /// </summary>
    public bool EstimatedAt(DateTime fightStart, double seconds)
    {
        if (!IsSelf || Track.T.Length == 0)
        {
            return false;
        }

        double t = seconds * 1000 - (Track.Start - fightStart).TotalMilliseconds;
        int hi = Array.BinarySearch(Track.T, (int)t);
        if (hi >= 0)
        {
            return false;
        }

        hi = ~hi;
        double before = hi > 0 ? t - Track.T[hi - 1] : double.PositiveInfinity;
        double after = hi < Track.T.Length ? Track.T[hi] - t : double.PositiveInfinity;
        return Math.Min(before, after) > ExactSeconds * 1000;
    }

    /// <summary>A reading this close in time still counts as where the character was.</summary>
    public const double ExactSeconds = 1.5;

    /// <summary>Where the entity was at <paramref name="seconds"/> from the start of the fight, between its two readings around it; null before its first reading, and after its last
    /// one for longer than <see cref="HoldSeconds"/>.</summary>
    public (double X, double Y)? At(DateTime fightStart, double seconds)
    {
        double ms = (Track.Start - fightStart).TotalMilliseconds + 0; // the track's origin relative to the fight start
        double t = seconds * 1000 - ms;
        var times = Track.T;
        if (times.Length == 0 || t < -HoldSeconds * 1000)
        {
            return null;
        }

        if (t <= times[0])
        {
            return (Track.X[0], Track.Y[0]);
        }

        int hi = Array.BinarySearch(times, (int)t);
        if (hi >= 0)
        {
            return (Track.X[hi], Track.Y[hi]);
        }

        hi = ~hi; // the first reading after t
        if (hi >= times.Length)
        {
            return t - times[^1] <= HoldSeconds * 1000 ? (Track.X[^1], Track.Y[^1]) : null;
        }

        int lo = hi - 1;
        double span = times[hi] - times[lo];
        if (span > MaxGapSeconds * 1000)
        {
            return t - times[lo] <= span / 2 ? (Track.X[lo], Track.Y[lo]) : (Track.X[hi], Track.Y[hi]); // too long a gap to draw a straight walk: it stays and jumps
        }

        double f = (t - times[lo]) / span;
        return (Track.X[lo] + (Track.X[hi] - Track.X[lo]) * f, Track.Y[lo] + (Track.Y[hi] - Track.Y[lo]) * f);
    }

    /// <summary>An entity not heard from for this long is no longer drawn.</summary>
    public const double HoldSeconds = 30;

    /// <summary>Between two readings further apart than this the entity is taken to have stayed, then jumped.</summary>
    public const double MaxGapSeconds = 8;
}

/// <summary>A skill a boss winds up: its area from where it was wound up, for as long as the windup lasts (<see cref="Start"/> to <see cref="End"/>, seconds from the start of the fight).</summary>
public sealed record ReplayArea(double Start, double End, int Actor, int SkillId, string SkillName, int Target, double Facing, double X, double Y, double? RadiusCm)
{
    /// <summary>The size of an area nobody has measured yet (drawn dashed).</summary>
    public const double DefaultRadiusCm = 450;

    public double Radius => RadiusCm ?? DefaultRadiusCm;
}

/// <summary>A boss's hit on a group member.</summary>
public sealed record ReplayHit(double At, int Attacker, int Victim, int SkillId);

/// <summary>A death on the replay's clock; Until is the revive (null: not revived within the fight).</summary>
public sealed record ReplayDeath(double At, int Entity, double? Until = null);

/// <summary>A boss's health over the fight: <see cref="Seconds"/> and the matching share (0-1).</summary>
public sealed record ReplayHp(int Entity, double[] Seconds, double[] Fraction);

/// <summary>Everything the replay map shows of one fight.</summary>
public sealed record ReplayData(
    string Title,
    DateTime Start,
    double DurationSeconds,
    IReadOnlyList<ReplayEntity> Entities,
    IReadOnlyList<ReplayArea> Areas,
    IReadOnlyList<ReplayHit> Hits,
    IReadOnlyList<ReplayDeath> Deaths,
    IReadOnlyList<StaggerPhase> Staggers,
    IReadOnlyList<ReplayHp> Hp)
{
    public bool HasData => Entities.Count(e => e.Role == ReplayRole.Player) > 0 && Entities.Any(e => e.Track.T.Length >= 2) && DurationSeconds > 1;
}

/// <summary>Works the replay out of a fight's <see cref="FightExtras"/>: who moved where, which boss skills were wound up and what they hit. Pure, so a test can run it.</summary>
public static class ReplayBuilder
{
    public static ReplayData Build(string title, FightExtras? extras, IReadOnlyList<DamageEvent> fightEvents, DateTime start, DateTime end,
        Func<int, (string Name, string ClassName, bool IsPlayer)> describe, Func<int, double?> radiusOf, Func<int, string> skillName, Func<int, bool>? isAdd = null, Func<int, bool>? isSelf = null)
    {
        double duration = Math.Max(1, (end - start).TotalSeconds);
        double Secs(DateTime at) => (at - start).TotalSeconds;
        var empty = new ReplayData(title, start, duration, Array.Empty<ReplayEntity>(), Array.Empty<ReplayArea>(), Array.Empty<ReplayHit>(), Array.Empty<ReplayDeath>(),
            Array.Empty<StaggerPhase>(), Array.Empty<ReplayHp>());
        if (extras?.Tracks is not { Count: > 0 } tracks)
        {
            return empty;
        }

        var entities = new List<ReplayEntity>();
        foreach (PositionTrack track in tracks.Where(t => t.T.Length >= 2))
        {
            var (name, cls, isPlayer) = describe(track.Entity);
            entities.Add(new ReplayEntity(track.Entity, name, cls, isPlayer ? ReplayRole.Player : isAdd?.Invoke(track.Entity) == true ? ReplayRole.Add : ReplayRole.Boss, track, isPlayer && isSelf?.Invoke(track.Entity) == true));
        }

        var bossIds = entities.Where(e => e.Role == ReplayRole.Boss).Select(e => e.Id).ToHashSet();
        var playerIds = entities.Where(e => e.Role == ReplayRole.Player).Select(e => e.Id).ToHashSet();
        var areas = (extras.Windups ?? Array.Empty<WindupRecord>())
            .Where(w => w.HasPosition && bossIds.Contains(w.Actor) && w.At >= start - TimeSpan.FromSeconds(2) && w.At <= end)
            .Select(w => new ReplayArea(Secs(w.At), Secs(w.At) + w.DurationMs / 1000.0, w.Actor, w.SkillId, skillName(w.SkillId), w.Target, w.Facing, w.X, w.Y, radiusOf(w.SkillId)))
            .OrderBy(a => a.Start).ToList();
        var hits = fightEvents.Where(e => !e.IsHeal && !e.IsTick && bossIds.Contains(e.SourceObjectId) && playerIds.Contains(e.TargetObjectId))
            .Select(e => new ReplayHit(Secs(e.Timestamp), e.SourceObjectId, e.TargetObjectId, e.SkillId)).OrderBy(h => h.At).ToList();
        var deaths = (extras.Deaths ?? Array.Empty<DeathRecord>()).Where(d => playerIds.Contains(d.Entity) && d.At >= start && d.At <= end)
            .Select(d => new ReplayDeath(Secs(d.At), d.Entity, d.RevivedAt is DateTime back ? Secs(back) : null)).OrderBy(d => d.At).ToList();
        var staggers = extras.Staggers.Where(s => s.Broke && s.At >= start && s.At <= end).Select(s => new StaggerPhase(Secs(s.At), Secs(s.At) + 5)).ToList();
        var hp = (extras.Hp ?? Array.Empty<HpSeries>()).Where(h => bossIds.Contains(h.Entity) && h.T.Length >= 2)
            .Select(h => new ReplayHp(h.Entity, h.T.Select(t => Secs(h.Start) + t / 1000.0).ToArray(), h.Permille.Select(p => p / 1000.0).ToArray())).ToList();
        return new ReplayData(title, start, duration, entities, areas, hits, deaths, staggers, hp);
    }
}
