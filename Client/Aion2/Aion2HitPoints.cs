namespace AionDPS.Aion2;

/// <summary>One hit-point reading of an entity, as the server sent it.</summary>
public readonly record struct HpSample(DateTime At, long Hp);

/// <summary>
/// Every entity's hit points over time, from the server's HP frame (see
/// Aion2FrameDecoder.DecodeHp), and the moments a monster came back to full health after having
/// been worn down - a wipe and retry, where the server keeps the boss's entity id. Without those
/// moments both attempts read as one fight against one target, and the damage summed over them
/// passes the boss's hit points (verified on a solo Krao Cave run, 2026-10-02: Ultimate Berk, 123,000
/// HP, 24,196 damage in the failed attempt, then back to 123,000 under the same id).
/// </summary>
public sealed class Aion2HitPoints
{
    /// <summary>A rise to the highest value seen counts as a reset only from below this share of
    /// it - small heals near full health are not a new attempt.</summary>
    private const double ResetFromBelow = 0.95;

    /// <summary>A single rise by this share of the maximum is a new attempt even without a reading at full health.</summary>
    private const double ResetRise = 0.10;

    /// <summary>Readings kept per entity; a long session drops its oldest half past this.</summary>
    private const int MaxSamplesPerEntity = 20_000;

    private readonly Dictionary<int, Track> _tracks = new();
    private readonly object _gate = new();

    /// <summary>The entities a hit-point reading exists for, and how many readings each has.</summary>
    public IReadOnlyDictionary<int, int> ReadingCounts()
    {
        lock (_gate)
        {
            return _tracks.ToDictionary(kv => kv.Key, kv => kv.Value.Samples.Count);
        }
    }

    public void Note(int entityId, DateTime at, long hp)
    {
        lock (_gate)
        {
            if (!_tracks.TryGetValue(entityId, out Track? track))
            {
                track = new Track();
                _tracks[entityId] = track;
            }

            if (track.Samples.Count > 0 && IsReset(track, hp))
            {
                track.Resets.Add(at);
            }

            track.HighestSeen = Math.Max(track.HighestSeen, hp);
            if (track.Samples.Count >= MaxSamplesPerEntity)
            {
                track.Samples.RemoveRange(0, MaxSamplesPerEntity / 2);
            }

            track.Samples.Add(new HpSample(at, hp));
        }
    }

    /// <summary>
    /// A new attempt: back to full health from a worn-down state, or - when the reset frame itself
    /// was never received (the player was dead or far away) and the next reading is already
    /// damaged - a single rise of at least <see cref="ResetRise"/> of the maximum. A boss's own
    /// healing stays far below that (Thamon's, 2026-10-04, was 2 %).
    /// </summary>
    private static bool IsReset(Track track, long hp)
    {
        long previous = track.Samples[^1].Hp;
        if (hp >= track.HighestSeen && previous < track.HighestSeen * ResetFromBelow)
        {
            return true;
        }

        return hp - previous >= track.HighestSeen * ResetRise;
    }

    /// <summary>The highest value seen - the maximum once the entity has been seen at full health
    /// (at its first reading, or after a reset); a lower bound when the meter joined mid-fight.</summary>
    public long? HighestSeen(int entityId)
    {
        lock (_gate)
        {
            return _tracks.TryGetValue(entityId, out Track? track) ? track.HighestSeen : null;
        }
    }

    /// <summary>The entity's most recent reading, or null before the first one.</summary>
    public HpSample? Latest(int entityId)
    {
        lock (_gate)
        {
            return _tracks.TryGetValue(entityId, out Track? track) && track.Samples.Count > 0 ? track.Samples[^1] : null;
        }
    }

    /// <summary>When the entity came back to full health from a worn-down state, oldest first.</summary>
    public IReadOnlyList<DateTime> ResetsOf(int entityId)
    {
        lock (_gate)
        {
            return _tracks.TryGetValue(entityId, out Track? track) ? track.Resets.ToList() : Array.Empty<DateTime>();
        }
    }

    /// <summary>The readings between two moments (inclusive), oldest first, plus the last one before
    /// <paramref name="from"/> - what the entity had when that window began.</summary>
    public IReadOnlyList<HpSample> SamplesAround(int entityId, DateTime from, DateTime to)
    {
        lock (_gate)
        {
            if (!_tracks.TryGetValue(entityId, out Track? track))
            {
                return Array.Empty<HpSample>();
            }

            int before = track.Samples.FindLastIndex(s => s.At < from);
            return track.Samples.Skip(Math.Max(before, 0)).TakeWhile(s => s.At <= to).ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _tracks.Clear();
        }
    }

    private sealed class Track
    {
        public long HighestSeen;
        public readonly List<HpSample> Samples = new();
        public readonly List<DateTime> Resets = new();
    }
}
