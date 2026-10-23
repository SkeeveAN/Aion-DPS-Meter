namespace AionDPS.Combat;

/// <summary>
/// Running totals over <see cref="LiveAggregator.Events"/>, kept up to date by looking only at the events that came in since
/// the last look. The list on screen used to be worked out again from the whole history once a second - per player, with a sort
/// in it - which a raid or a hundred fights at once turned into seconds per refresh. Everything here answers exactly what
/// that rescan answered (the stress test compares the two), so a total never differs, only the cost does.
/// <para>
/// The index only ever appends. Anything that rewrites events already counted (Clear, a summon credited to its owner,
/// two ids merged) changes <see cref="LiveAggregator.Version"/>, and the index then starts over from the first event.
/// </para>
/// </summary>
public sealed class DamageIndex
{
    /// <summary>Damage dealt by one source: total and the time of its first and last hit (the rate's base).</summary>
    public sealed class Stats
    {
        public long Sum;
        /// <summary>How many hits were counted.</summary>
        public int Hits;
        /// <summary>The part of <see cref="Sum"/> that is estimated (see <see cref="DamageEvent.Estimated"/>).</summary>
        public long EstimatedSum;
        public DateTime First = DateTime.MaxValue;
        public DateTime Last = DateTime.MinValue;

        internal void Add(DamageEvent ev)
        {
            Sum += ev.Amount;
            Hits++;
            if (ev.Estimated)
            {
                EstimatedSum += ev.Amount;
            }

            if (ev.Timestamp < First)
            {
                First = ev.Timestamp;
            }

            if (ev.Timestamp > Last)
            {
                Last = ev.Timestamp;
            }
        }

        /// <summary>Wall-clock rate as <see cref="DpsCalculator.AllDpsWallClock"/> gives it: null for a single instant.</summary>
        public double? Rate
        {
            get
            {
                double seconds = (Last - First).TotalSeconds;
                return seconds > 0 ? Sum / seconds : null;
            }
        }
    }

    private sealed class TargetLine
    {
        public readonly List<long> Ticks = new();
        public readonly List<long> Total = new(); // running sum up to and including that hit
    }

    private LiveAggregator? _aggregator;
    private int _version = -1;
    private int _consumed;

    // Every damage event (heals are not damage).
    public readonly Dictionary<int, Stats> BySource = new();
    public readonly Dictionary<int, long> TakenByTarget = new();
    public long DamageCount { get; private set; }
    public DateTime DamageFirst { get; private set; } = DateTime.MaxValue;
    public DateTime DamageLast { get; private set; } = DateTime.MinValue;

    // Damage whose target is a player (PvP). A target counts once the meter knows it as a player, which can come after
    // its first hits: until then they wait in _pending, and are counted the moment the target is known.
    public readonly Dictionary<int, Stats> PvpBySource = new();
    public long PvpCount { get; private set; }
    public DateTime PvpFirst { get; private set; } = DateTime.MaxValue;
    public DateTime PvpLast { get; private set; } = DateTime.MinValue;

    /// <summary>What a target that is no known player has taken so far, per source. Only totals, counts and the first and last
    /// time are kept: that is all PvP counting asks of a hit, so the hits themselves need not be held a second time (nearly every
    /// target is a monster and never becomes a player; a copy of each of its hits would double the memory of the index).</summary>
    private sealed class PendingTarget
    {
        public long Count;
        public readonly Dictionary<int, Stats> BySource = new();
    }

    private readonly Dictionary<int, PendingTarget> _pending = new();

    // Per target, the damage it took in time order, to answer "how much between a and b" without a scan.
    private readonly Dictionary<int, TargetLine> _taken = new();

    /// <summary>Heals, in arrival order (they are few next to the hits).</summary>
    public readonly List<DamageEvent> Heals = new();

    /// <summary>The distinct targets of damage so far.</summary>
    public readonly HashSet<int> Targets = new();

    /// <summary>False once an event arrived earlier than one before it for the same target: the range sums assume
    /// time order, so PvP then falls back to the full rescan.</summary>
    public bool TimeOrdered { get; private set; } = true;

    /// <summary>Takes in the events added since the last call. <paramref name="isPlayer"/> is the directory's answer
    /// to "is this id a player", asked for new targets and for the ones still waiting.</summary>
    public void Update(LiveAggregator aggregator, Func<int, bool> isPlayer)
    {
        IReadOnlyList<DamageEvent> events = aggregator.Events;
        if (!ReferenceEquals(aggregator, _aggregator) || aggregator.Version != _version || events.Count < _consumed)
        {
            Reset();
            _aggregator = aggregator;
            _version = aggregator.Version;
        }

        var known = new Dictionary<int, bool>(); // the directory takes a lock per question: once per target and call
        bool IsPlayer(int id) => known.TryGetValue(id, out bool v) ? v : known[id] = isPlayer(id);

        for (; _consumed < events.Count; _consumed++)
        {
            DamageEvent ev = events[_consumed];
            if (ev.IsHeal)
            {
                Heals.Add(ev);
                continue;
            }

            Count(BySource, ev.SourceObjectId, ev);
            TakenByTarget[ev.TargetObjectId] = TakenByTarget.GetValueOrDefault(ev.TargetObjectId) + ev.Amount;
            Targets.Add(ev.TargetObjectId);
            DamageCount++;
            DamageFirst = ev.Timestamp < DamageFirst ? ev.Timestamp : DamageFirst;
            DamageLast = ev.Timestamp > DamageLast ? ev.Timestamp : DamageLast;

            if (!_taken.TryGetValue(ev.TargetObjectId, out TargetLine? line))
            {
                _taken[ev.TargetObjectId] = line = new TargetLine();
            }

            if (line.Ticks.Count > 0 && ev.Timestamp.Ticks < line.Ticks[^1])
            {
                TimeOrdered = false;
            }

            line.Ticks.Add(ev.Timestamp.Ticks);
            line.Total.Add((line.Total.Count > 0 ? line.Total[^1] : 0) + ev.Amount);

            if (IsPlayer(ev.TargetObjectId))
            {
                CountPvp(ev);
            }
            else
            {
                if (!_pending.TryGetValue(ev.TargetObjectId, out PendingTarget? waiting))
                {
                    _pending[ev.TargetObjectId] = waiting = new PendingTarget();
                }

                waiting.Count++;
                Count(waiting.BySource, ev.SourceObjectId, ev);
            }
        }

        // Targets that have become known as players since: their earlier hits count from now on, as a rescan would count them.
        if (_pending.Count > 0)
        {
            foreach (int target in _pending.Keys.ToList())
            {
                if (!IsPlayer(target))
                {
                    continue;
                }

                MergePending(_pending[target]);
                _pending.Remove(target);
            }
        }
    }

    /// <summary>Damage <paramref name="target"/> took between the two times, both ends included.</summary>
    public long TakenBetween(int target, DateTime from, DateTime to)
    {
        if (!_taken.TryGetValue(target, out TargetLine? line) || line.Ticks.Count == 0)
        {
            return 0;
        }

        int first = LowerBound(line.Ticks, from.Ticks);
        int after = LowerBound(line.Ticks, to.Ticks + 1);
        if (after <= first)
        {
            return 0;
        }

        return line.Total[after - 1] - (first > 0 ? line.Total[first - 1] : 0);
    }

    private static int LowerBound(List<long> ticks, long value)
    {
        int lo = 0, hi = ticks.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (ticks[mid] < value)
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

    /// <summary>Counts what a target took before it was known as a player: the same totals, counts and times as counting its hits one by one.</summary>
    private void MergePending(PendingTarget waiting)
    {
        foreach ((int source, Stats part) in waiting.BySource)
        {
            if (!PvpBySource.TryGetValue(source, out Stats? stats))
            {
                PvpBySource[source] = stats = new Stats();
            }

            stats.Sum += part.Sum;
            stats.Hits += part.Hits;
            stats.EstimatedSum += part.EstimatedSum;
            stats.First = part.First < stats.First ? part.First : stats.First;
            stats.Last = part.Last > stats.Last ? part.Last : stats.Last;
            PvpFirst = part.First < PvpFirst ? part.First : PvpFirst;
            PvpLast = part.Last > PvpLast ? part.Last : PvpLast;
        }

        PvpCount += waiting.Count;
    }

    private void CountPvp(DamageEvent ev)
    {
        Count(PvpBySource, ev.SourceObjectId, ev);
        PvpCount++;
        PvpFirst = ev.Timestamp < PvpFirst ? ev.Timestamp : PvpFirst;
        PvpLast = ev.Timestamp > PvpLast ? ev.Timestamp : PvpLast;
    }

    private static void Count(Dictionary<int, Stats> into, int source, DamageEvent ev)
    {
        if (!into.TryGetValue(source, out Stats? stats))
        {
            into[source] = stats = new Stats();
        }

        stats.Add(ev);
    }

    private void Reset()
    {
        _consumed = 0;
        BySource.Clear();
        TakenByTarget.Clear();
        Targets.Clear();
        PvpBySource.Clear();
        _pending.Clear();
        _taken.Clear();
        Heals.Clear();
        DamageCount = PvpCount = 0;
        DamageFirst = PvpFirst = DateTime.MaxValue;
        DamageLast = PvpLast = DateTime.MinValue;
        TimeOrdered = true;
    }
}
