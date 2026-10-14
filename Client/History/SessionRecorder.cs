using AionDPS.Aion2;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.History;

/// <summary>What the server said about one target's hit points during a stay: the highest value seen, the readings and the moments it came back to full health.</summary>
public sealed record TargetHp(long Highest, IReadOnlyList<HpSample> Samples, IReadOnlyList<DateTime> Resets);

public enum SessionKind
{
    Map,
    Instance,
}

/// <summary>One stay in one map or instance: from the zone change that began it to the one that ended it. Its events are its own
/// (a LiveAggregator each), so any session can be shown, saved or dropped without touching the others.</summary>
public sealed class SessionInfo
{
    public SessionInfo(int number, int zoneId, SessionKind kind, DateTime start)
    {
        Number = number;
        ZoneId = zoneId;
        Kind = kind;
        Start = start;
    }

    /// <summary>Counts up for the life of the recorder; the stable handle for the menu.</summary>
    public int Number { get; }

    /// <summary>The game's zone id; 0 for the stretch before the first zone frame (the meter started in the middle of a map).</summary>
    public int ZoneId { get; }

    public SessionKind Kind { get; }

    public DateTime Start { get; }

    /// <summary>Null while the player is still there.</summary>
    public DateTime? End { get; internal set; }

    public bool IsOpen => End is null;

    public LiveAggregator Data { get; } = new();

    /// <summary>Names and classes as they were while the session ran: the entity directory forgets or reuses ids later.</summary>
    public Dictionary<int, string> Names { get; } = new();

    public Dictionary<int, string> Classes { get; } = new();

    /// <summary>The hit points of the stay's big targets (see <see cref="SessionRecorder.CaptureHp"/>); what the plausibility check holds the damage against.</summary>
    public Dictionary<int, TargetHp> Hp { get; } = new();

    /// <summary>The boss rages the server announced during the stay (see <see cref="EnrageMark"/>).</summary>
    public List<EnrageMark> Enrages { get; } = new();

    /// <summary>The finished analysis, kept while the session has not changed (see SessionAnalyzer).</summary>
    internal (int EventCount, SessionAnalysis Result)? CachedAnalysis { get; set; }

    /// <summary>The rotation, effect, stagger and replay data that came with a file the session was loaded from (null for a live session: that reads them from the capture).</summary>
    public Combat.FightExtras? Extras { get; set; }

    /// <summary>Kept by the user (written to a file or loaded from one): never dropped by the age or memory limits.</summary>
    public bool IsSaved { get; set; }

    /// <summary>Loaded from a file. Its ids belong to another run of the game, so it stays out of the "all" view and of id merging.</summary>
    public bool FromFile { get; init; }

    /// <summary>The time spent elsewhere while the stay was open again: the instance runs between two visits of the same map (see <see cref="SessionRecorder.Enter"/>).</summary>
    public TimeSpan Away { get; internal set; }

    /// <summary>The stay's length without the time spent away.</summary>
    public TimeSpan Duration(DateTime now) => (End ?? now) - Start - Away;
}

/// <summary>
/// Cuts the endless stream of damage events into sessions at every zone change (map to map, map to instance and back), and keeps them
/// apart from what the meter shows: the live view can be cleared or switched to another session while this goes on recording.
/// Stays that did nothing (no hit, under <see cref="TrivialStay"/>: a pass through a portal hall) are dropped when they end.
/// </summary>
public sealed class SessionRecorder
{
    /// <summary>A stay shorter than this without a single hit is not worth a menu entry.</summary>
    public static readonly TimeSpan TrivialStay = TimeSpan.FromMinutes(2);

    /// <summary>Ended sessions older than this are forgotten (saving one keeps it).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    /// <summary>The most events held in memory over all sessions; the oldest ended sessions go first.</summary>
    public int MaxEvents { get; init; } = 2_000_000;

    private readonly Func<int, SessionKind> _kindOf;
    private readonly List<SessionInfo> _sessions = new();
    private int _nextNumber = 1;

    public SessionRecorder(Func<int, SessionKind> kindOf) => _kindOf = kindOf;

    /// <summary>Gives the name and class an id has right now; the names are kept with the session because the entity directory
    /// forgets or reuses ids later. A name that is still a placeholder ("Gladiator #331") is replaced when the real one arrives
    /// (<see cref="RefreshNames"/>) and a last time when the stay ends.</summary>
    public Func<int, (string? Name, string? Class)>? Identify { get; set; }

    /// <summary>Oldest first; the last one is <see cref="Current"/> once anything was recorded.</summary>
    public IReadOnlyList<SessionInfo> Sessions => _sessions;

    public SessionInfo? Current => _sessions.Count > 0 && _sessions[^1].IsOpen ? _sessions[^1] : null;

    /// <summary>Raised when a session starts or ends (not for every event).</summary>
    public event Action? Changed;

    /// <summary>
    /// Takes one poll's worth: the events before a zone change belong to the stay it ended, the ones after to the new stay.
    /// </summary>
    public void Feed(CombatBatch batch)
    {
        // A rage belongs to the stay it came in (the newest stay that began before it).
        foreach (EnrageMark mark in batch.EnrageMarks)
        {
            _sessions.Where(s => s.Start <= mark.Timestamp && (s.End is null || s.End >= mark.Timestamp)).MaxBy(s => s.Start)?.Enrages.Add(mark);
        }

        IReadOnlyList<DamageEvent> events = batch.Damage;
        int next = 0;
        foreach (ZoneChange zone in batch.ZoneChanges)
        {
            int end = next;
            while (end < events.Count && events[end].Timestamp < zone.Timestamp)
            {
                end++;
            }

            AddRange(events, next, end);
            next = end;
            Enter(zone);
        }

        AddRange(events, next, events.Count);
    }

    /// <summary>Starts a stay in <paramref name="zone"/> and ends the one before. Coming back to a map after instance runs (and nothing else in between)
    /// picks up the stay the player left there, so the totals of that map are still on show; it moves to the end of the list as the current one.</summary>
    public void Enter(ZoneChange zone)
    {
        if (Current is { } open)
        {
            if (open.ZoneId == zone.ZoneId && !zone.Restart)
            {
                return;
            }

            Close(open, zone.Timestamp);
        }

        SessionKind kind = _kindOf(zone.ZoneId);
        if (kind == SessionKind.Map && ResumableStay(zone.ZoneId) is { } resumed)
        {
            resumed.Away += zone.Timestamp - resumed.End!.Value;
            resumed.End = null;
            _sessions.Remove(resumed);
            _sessions.Add(resumed);
            Changed?.Invoke();
            return;
        }

        _sessions.Add(new SessionInfo(_nextNumber++, zone.ZoneId, kind, zone.Timestamp));
        Prune(zone.Timestamp);
        Changed?.Invoke();
    }

    /// <summary>The latest stay of the live stream, looking past instance runs: it counts when it is this map and still unsaved.</summary>
    private SessionInfo? ResumableStay(int zoneId)
    {
        for (int i = _sessions.Count - 1; i >= 0; i--)
        {
            SessionInfo s = _sessions[i];
            if (s.FromFile || s.Kind == SessionKind.Instance)
            {
                continue;
            }

            return s.ZoneId == zoneId && !s.IsOpen && !s.IsSaved ? s : null;
        }

        return null;
    }

    /// <summary>Ends the open stay (the meter is closing or the capture stopped).</summary>
    public void CloseCurrent(DateTime now)
    {
        if (Current is { } open)
        {
            Close(open, now);
            Changed?.Invoke();
        }
    }

    /// <summary>Gives the hit-point readings of a target between two moments; the meter hands in what the capture's entity directory has.</summary>
    public Func<int, DateTime, DateTime, TargetHp?>? HpSource { get; set; }

    /// <summary>The targets that took the most damage in a stay and are big enough to be bosses: at least <see cref="BossMinDamage"/> and
    /// <see cref="BossMinHits"/> hits, players excluded.</summary>
    public static List<int> BossCandidates(SessionInfo session, int max = 5)
    {
        return session.Data.Events
            .Where(ev => !ev.IsHeal && ev.TargetObjectId != ev.SourceObjectId && !session.Classes.ContainsKey(ev.TargetObjectId))
            .GroupBy(ev => ev.TargetObjectId)
            .Select(g => (Id: g.Key, Damage: g.Sum(ev => ev.Amount), Hits: g.Count()))
            .Where(t => t.Damage >= BossMinDamage && t.Hits >= BossMinHits)
            .OrderByDescending(t => t.Damage)
            .Take(max)
            .Select(t => t.Id)
            .ToList();
    }

    public const long BossMinDamage = 50_000;
    public const int BossMinHits = 10;

    /// <summary>Takes the hit points of the stay's big targets from the capture, while it still has them; a stay loaded from a file brought its own.</summary>
    public void CaptureHp(SessionInfo session)
    {
        if (HpSource is not { } source || session.FromFile || session.Data.Events.Count == 0)
        {
            return;
        }

        DateTime end = session.End ?? session.Data.Events[^1].Timestamp;
        foreach (int id in BossCandidates(session))
        {
            if (source(id, session.Start, end) is { } hp)
            {
                session.Hp[id] = hp;
            }
        }
    }

    private void Close(SessionInfo session, DateTime end)
    {
        RefreshNames(session);
        session.End = end;
        CaptureHp(session);
        if (session.Data.Events.Count == 0 && end - session.Start < TrivialStay)
        {
            _sessions.Remove(session);
        }
    }

    /// <summary>Asks again for the name and class of every id of the open stay (names arrive some time after the first hit).</summary>
    public void RefreshNames()
    {
        if (Current is { } open)
        {
            RefreshNames(open);
        }
    }

    private void RefreshNames(SessionInfo session)
    {
        if (Identify is not { } identify)
        {
            return;
        }

        foreach (int id in session.Names.Keys.ToList())
        {
            Remember(session, id, identify, replace: true);
        }
    }

    private void AddRange(IReadOnlyList<DamageEvent> events, int from, int to)
    {
        if (to <= from)
        {
            return;
        }

        SessionInfo session = Current ?? Open(events[from].Timestamp);
        var slice = new List<DamageEvent>(to - from);
        for (int i = from; i < to; i++)
        {
            slice.Add(events[i]);
            if (Identify is { } identify)
            {
                Remember(session, events[i].SourceObjectId, identify);
                Remember(session, events[i].TargetObjectId, identify);
            }
        }

        session.Data.IngestEvents(slice);
        Trim();
    }

    /// <summary>The first events came before any zone frame: that stay is of an unknown map, from the first hit on.</summary>
    private SessionInfo Open(DateTime start)
    {
        var session = new SessionInfo(_nextNumber++, 0, SessionKind.Map, start);
        _sessions.Add(session);
        Changed?.Invoke();
        return session;
    }

    private static void Remember(SessionInfo session, int id, Func<int, (string? Name, string? Class)> identify, bool replace = false)
    {
        if (!replace && session.Names.ContainsKey(id))
        {
            return;
        }

        (string? name, string? cls) = identify(id);
        if (name is not null)
        {
            session.Names[id] = name;
            if (cls is { Length: > 0 } and not "?")
            {
                session.Classes[id] = cls;
            }
        }
    }

    /// <summary>The player got a new combat id (a map change): the same player in every session, so the "All" view is one row.</summary>
    public void MergeIds(int from, int to)
    {
        foreach (SessionInfo session in _sessions.Where(s => !s.FromFile))
        {
            session.Data.MergeIds(from, to);
            if (session.Names.Remove(from, out string? name))
            {
                session.Names.TryAdd(to, name);
            }

            if (session.Classes.Remove(from, out string? cls))
            {
                session.Classes.TryAdd(to, cls);
            }
        }
    }

    /// <summary>A summon's earlier hits go to its owner once the owner is known.</summary>
    public void Reattribute(int summon, int owner)
    {
        foreach (SessionInfo session in _sessions.Where(s => !s.FromFile))
        {
            session.Data.Reattribute(summon, owner);
        }
    }

    /// <summary>The events of all sessions (or of one) in time order, for the "All" view.</summary>
    public List<DamageEvent> AllEvents() => _sessions.Where(s => !s.FromFile).SelectMany(s => s.Data.Events).OrderBy(e => e.Timestamp).ToList();

    public List<DamageEvent> AllShields() => _sessions.Where(s => !s.FromFile).SelectMany(s => s.Data.Shields).OrderBy(e => e.Timestamp).ToList();

    /// <summary>A session made outside the live stream (loaded from a file): it goes in by its start time, before the open stay.</summary>
    public SessionInfo Import(int zoneId, SessionKind kind, DateTime start, DateTime? end)
    {
        var session = new SessionInfo(_nextNumber++, zoneId, kind, start) { FromFile = true, IsSaved = true, End = end ?? start };
        int at = Current is null ? _sessions.Count : _sessions.Count - 1;
        while (at > 0 && _sessions[at - 1].Start > start)
        {
            at--;
        }

        _sessions.Insert(at, session);
        Changed?.Invoke();
        return session;
    }

    /// <summary>Drops a finished session (the open one cannot be removed).</summary>
    public bool Remove(SessionInfo session)
    {
        if (session.IsOpen || !_sessions.Remove(session))
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    private void Prune(DateTime now)
    {
        int removed = _sessions.RemoveAll(s => !s.IsOpen && !s.IsSaved && now - s.End!.Value > Retention);
        if (removed > 0)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Keeps the memory in check: drops the oldest ended sessions while there are too many events.</summary>
    private void Trim()
    {
        long total = 0;
        foreach (SessionInfo session in _sessions)
        {
            total += session.Data.Events.Count;
        }

        bool changed = false;
        while (total > MaxEvents)
        {
            int oldest = _sessions.FindIndex(s => !s.IsOpen && !s.IsSaved);
            if (oldest < 0)
            {
                break;
            }

            total -= _sessions[oldest].Data.Events.Count;
            _sessions.RemoveAt(oldest);
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }
}
