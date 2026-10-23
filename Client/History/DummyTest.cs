using System.IO;
using System.Text.Json;
using AionDPS.Aion2.Protocol;
using AionDPS.Combat;

namespace AionDPS.History;

public enum DummyState
{
    /// <summary>No test is waiting or running.</summary>
    Idle,

    /// <summary>Started, but the clock waits for the first hit, skill or effect of the player.</summary>
    Armed,

    Running,

    /// <summary>The time is up (or it was stopped): the overlay says so for a while.</summary>
    Finished,
}

/// <summary>One run at the training dummy: the player's own hits, skills and effects from the first one until the planned time was over.</summary>
public sealed class DummyRun
{
    public int Number { get; init; }

    /// <summary>The first hit, skill or effect: the clock began here.</summary>
    public DateTime Start { get; internal set; }

    /// <summary>The time the player asked for.</summary>
    public TimeSpan Planned { get; init; }

    /// <summary>How long the run really lasted: the planned time, shorter when it was stopped early.</summary>
    public TimeSpan Length { get; internal set; }

    public DateTime End => Start + Length;

    public string Note { get; set; } = "";

    /// <summary>The combat id the run was recorded for (the player).</summary>
    public int PlayerId { get; internal set; }

    public List<DamageEvent> Events { get; } = new();

    public List<SkillUse> Uses { get; } = new();

    public List<BuffEvent> Buffs { get; } = new();

    public Dictionary<int, string> Names { get; } = new();

    /// <summary>Uses and effects have been read from the capture: nothing more is added to the run.</summary>
    public bool Closed { get; internal set; }
}

/// <summary>
/// The dummy test: the player stands at a training dummy for a set time and tries a skill rotation. Each Start makes a new run; the clock begins with the
/// first own hit, skill or effect after the Start, and what happens after the time is up is not part of the run. The meter's own sessions are not touched:
/// they go on recording all the time. Pure (the caller feeds it events and the time), so a test can run it.
/// </summary>
public sealed class DummyTest
{
    /// <summary>The hits of the last second still belong to a run for a moment after its time is up: they arrive with the next poll.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(2.5);

    /// <summary>How long "Finished" stays on the overlay.</summary>
    public static readonly TimeSpan FinishedShown = TimeSpan.FromSeconds(20);

    private readonly List<DummyRun> _runs = new();
    private int _nextNumber = 1;
    private TimeSpan _planned = TimeSpan.FromMinutes(2);
    private string _note = "";
    private DateTime _armedAt;
    private DateTime _finishedAt;
    private DummyRun? _current;

    public IReadOnlyList<DummyRun> Runs => _runs;

    public DummyState State { get; private set; }

    /// <summary>The run being recorded (or just finished); null while armed or idle.</summary>
    public DummyRun? Current => _current;

    public TimeSpan Planned => _planned;

    public DateTime ArmedAt => _armedAt;

    /// <summary>Raised when a run starts or ends, the state changes or a run is changed or removed (not for every event).</summary>
    public event Action? Changed;

    /// <summary>Reads the skills and effects of the player into a run that is over; set by the caller, who holds the capture.</summary>
    public Action<DummyRun>? Capture { get; set; }

    public bool CanStart => State != DummyState.Running;

    /// <summary>Waits for the first own hit, skill or effect; from then on <paramref name="planned"/> counts down.</summary>
    public void Arm(TimeSpan planned, string note, DateTime now)
    {
        if (State == DummyState.Running)
        {
            return;
        }

        _planned = planned;
        _note = note;
        _armedAt = now;
        _current = null;
        State = DummyState.Armed;
        Changed?.Invoke();
    }

    /// <summary>Takes back an armed test that never started.</summary>
    public void Cancel()
    {
        if (State == DummyState.Armed)
        {
            State = DummyState.Idle;
            Changed?.Invoke();
        }
    }

    /// <summary>Ends a running test early: the run keeps what happened until now.</summary>
    public void Stop(DateTime now)
    {
        if (State == DummyState.Armed)
        {
            Cancel();
            return;
        }

        if (State == DummyState.Running && _current is { } run)
        {
            run.Length = now > run.Start ? now - run.Start : TimeSpan.Zero;
            Finish(now);
        }
    }

    /// <summary>The time left: the planned time while waiting, counting down while running, nothing after it.</summary>
    public TimeSpan Remaining(DateTime now) => State switch
    {
        DummyState.Armed => _planned,
        DummyState.Running when _current is { } run => run.End > now ? run.End - now : TimeSpan.Zero,
        _ => TimeSpan.Zero,
    };

    /// <summary>Takes a poll's damage events: the player's own start the clock while armed, and while running the ones inside the time are kept.</summary>
    public void Feed(IReadOnlyList<DamageEvent> events, Func<int, bool> isOwn)
    {
        if (State == DummyState.Idle || events.Count == 0)
        {
            return;
        }

        foreach (DamageEvent ev in events.OrderBy(e => e.Timestamp))
        {
            if (ev.IsHeal || !isOwn(ev.SourceObjectId))
            {
                continue;
            }

            if (State == DummyState.Armed && ev.Timestamp >= _armedAt)
            {
                Begin(ev.Timestamp, ev.SourceObjectId);
            }

            if (_current is { Closed: false } run && ev.Timestamp >= run.Start && ev.Timestamp <= run.End)
            {
                run.Events.Add(ev);
            }
        }
    }

    /// <summary>A skill or an effect of the player at <paramref name="at"/>: it starts the clock when armed, and moves the start of a run that began with a hit
    /// back to it (the cast comes before the hit, but is read later).</summary>
    public void NoteActivity(DateTime at, int playerId)
    {
        if (at < _armedAt)
        {
            return;
        }

        if (State == DummyState.Armed)
        {
            Begin(at, playerId);
        }
        else if (State == DummyState.Running && _current is { } run && at < run.Start)
        {
            run.Start = at;
        }
    }

    /// <summary>Moves the clock on: ends a run whose time is up, closes it a moment later, and clears the "Finished" after a while.</summary>
    public void Tick(DateTime now)
    {
        if (State == DummyState.Running && _current is { } run && now >= run.End)
        {
            Finish(now);
        }

        if (_current is { Closed: false } open && State == DummyState.Finished && now >= open.End + Grace)
        {
            Close(open);
        }

        if (State == DummyState.Finished && now - _finishedAt > FinishedShown && _current is { Closed: true })
        {
            State = DummyState.Idle;
            Changed?.Invoke();
        }
    }

    /// <summary>Drops a run (not the one being recorded).</summary>
    public bool Remove(DummyRun run)
    {
        if (ReferenceEquals(run, _current) && !run.Closed)
        {
            return false;
        }

        if (!_runs.Remove(run))
        {
            return false;
        }

        if (ReferenceEquals(run, _current))
        {
            _current = null;
            if (State == DummyState.Finished)
            {
                State = DummyState.Idle;
            }
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Tells the test that a run's note was edited (the list saves it).</summary>
    public void NoteEdited() => Changed?.Invoke();

    /// <summary>Takes runs read from the file (before anything else happens).</summary>
    public void Load(IEnumerable<DummyRun> runs)
    {
        _runs.AddRange(runs);
        _nextNumber = _runs.Count == 0 ? 1 : _runs.Max(r => r.Number) + 1;
    }

    private void Begin(DateTime at, int playerId)
    {
        var run = new DummyRun { Number = _nextNumber++, Start = at, Planned = _planned, Length = _planned, Note = _note, PlayerId = playerId };
        _runs.Add(run);
        _current = run;
        State = DummyState.Running;
        Changed?.Invoke();
    }

    private void Finish(DateTime now)
    {
        _finishedAt = now;
        State = DummyState.Finished;
        Changed?.Invoke();
    }

    private void Close(DummyRun run)
    {
        run.Closed = true;
        Capture?.Invoke(run);
        Changed?.Invoke();
    }
}

/// <summary>The numbers of one run, the ones the player's details show: damage per second, the hits and the skills used.</summary>
public sealed record DummyStats(
    double Seconds, long Total, int Hits, double AvgDps, double MinDps, double MaxDps, long MinHit, long MaxHit, double AvgHit, double CritPercent, int SkillUses);

public static class DummyAnalysis
{
    /// <summary>
    /// The chain skills of a run: how many uses of a skill with a follow-up were followed by it within its window (3 s, a third of a second more for the 0.1 s the use times are
    /// counted in), A rotation that misses its chains is why the dummy's damage stays below what the skills could do.
    /// </summary>
    public static (int Offered, int Taken) Chains(DummyRun run)
    {
        var uses = run.Uses.Where(u => !Aion2Artwork.IsPassive(u.SkillId)).OrderBy(u => u.At).ToList();
        int offered = 0, taken = 0;
        foreach (SkillUse use in uses)
        {
            foreach ((int next, int windowMs) in Aion2SkillChains.After(use.SkillId))
            {
                offered++;
                DateTime until = use.At.AddMilliseconds(windowMs + 300);
                if (uses.Any(u => u.SkillId == next && u.At > use.At && u.At <= until))
                {
                    taken++;
                }
            }
        }

        return (offered, taken);
    }

    public static DummyStats Summarize(DummyRun run)
    {
        double seconds = Math.Max(1, run.Length.TotalSeconds);
        long total = run.Events.Sum(e => e.Amount);
        var hits = run.Events.Where(e => !e.IsTick).ToList();
        int hitCount = hits.Sum(e => Math.Max(1, e.HitCount));

        // damage in every whole second of the run: the slowest and the fastest second
        int buckets = Math.Max(1, (int)Math.Floor(run.Length.TotalSeconds));
        var perSecond = new long[buckets];
        foreach (DamageEvent e in run.Events)
        {
            int at = (int)Math.Floor((e.Timestamp - run.Start).TotalSeconds);
            perSecond[Math.Clamp(at, 0, buckets - 1)] += e.Amount;
        }

        long minHit = hits.Count > 0 ? hits.Min(e => e.Amount / Math.Max(1, e.HitCount)) : 0;
        long maxHit = hits.Count > 0 ? hits.Max(e => e.Amount / Math.Max(1, e.HitCount)) : 0;
        double crit = hits.Count > 0 ? 100.0 * hits.Count(e => e.IsCritical) / hits.Count : 0;
        return new DummyStats(seconds, total, hitCount, total / seconds, perSecond.Min(), perSecond.Max(), minHit, maxHit,
            hitCount > 0 ? (double)hits.Sum(e => e.Amount) / hitCount : 0, crit, run.Uses.Count);
    }

    /// <summary>One line of the timeline: a skill (a tick per use) or an effect (a bar per stretch it was on), for each run that was compared.</summary>
    public sealed record Lane(string Name, int SkillId, bool IsEffect, IReadOnlyList<IReadOnlyList<(double Start, double End)>> PerRun);

    private const int MaxEffectLanes = 12;

    /// <summary>The skills and effects of the compared runs on one time axis (seconds from each run's start).</summary>
    public static List<Lane> Timeline(IReadOnlyList<DummyRun> runs)
    {
        var skills = new Dictionary<string, (int Id, double First, List<(double, double)>[] Uses)>();
        for (int r = 0; r < runs.Count; r++)
        {
            foreach (SkillUse use in runs[r].Uses.OrderBy(u => u.At))
            {
                if (Aion2Artwork.IsPassive(use.SkillId))
                {
                    continue;
                }

                string name = Aion2SkillNames.NameOf(use.SkillId);
                double at = (use.At - runs[r].Start).TotalSeconds;
                if (at < -1 || at > runs[r].Length.TotalSeconds + 1)
                {
                    continue;
                }

                if (!skills.TryGetValue(name, out var entry))
                {
                    entry = (use.SkillId, at, Enumerable.Range(0, runs.Count).Select(_ => new List<(double, double)>()).ToArray());
                }

                entry.Uses[r].Add((Math.Max(0, at), Math.Max(0, at)));
                skills[name] = (entry.Id, Math.Min(entry.First, at), entry.Uses);
            }
        }

        var lanes = new List<Lane>();
        foreach (var (name, entry) in skills.OrderBy(kv => kv.Value.First))
        {
            lanes.Add(new Lane(Aion2SkillNames.Display(name), entry.Id, false, entry.Uses.Select(l => (IReadOnlyList<(double, double)>)l).ToList()));
        }

        var effects = new Dictionary<string, (int Id, List<(double, double)>[] Spans)>();
        for (int r = 0; r < runs.Count; r++)
        {
            foreach (var (skillId, start, end) in EffectSpans(runs[r]))
            {
                string name = Aion2SkillNames.NameOf(skillId);
                if (!effects.TryGetValue(name, out var entry))
                {
                    entry = (skillId, Enumerable.Range(0, runs.Count).Select(_ => new List<(double, double)>()).ToArray());
                    effects[name] = entry;
                }

                entry.Spans[r].Add((start, end));
            }
        }

        foreach (var (name, entry) in effects
                     .OrderByDescending(kv => kv.Value.Spans.Max(l => l.Sum(s => s.Item2 - s.Item1)))
                     .Take(MaxEffectLanes))
        {
            lanes.Add(new Lane(Aion2SkillNames.Display(name), entry.Id, true, entry.Spans.Select(l => (IReadOnlyList<(double, double)>)l).ToList()));
        }

        return lanes;
    }

    /// <summary>The stretches each effect on the player was on during the run (seconds from the start); those under half a second in all are left out.</summary>
    private static List<(int SkillId, double Start, double End)> EffectSpans(DummyRun run)
    {
        double length = run.Length.TotalSeconds;
        var spans = new List<(int, double, double)>();
        var open = new Dictionary<int, (DateTime Start, int Skill, DateTime? Expect)>();

        void Close(int instance, DateTime at)
        {
            if (open.Remove(instance, out var o))
            {
                double s = Math.Max(0, (o.Start - run.Start).TotalSeconds), e = Math.Min(length, (at - run.Start).TotalSeconds);
                if (e > s)
                {
                    spans.Add((o.Skill, s, e));
                }
            }
        }

        foreach (BuffEvent ev in run.Buffs.Where(b => b.Target == run.PlayerId).OrderBy(b => b.At))
        {
            foreach (int instance in open.Where(kv => kv.Value.Expect is DateTime expect && expect < ev.At).Select(kv => kv.Key).ToList())
            {
                Close(instance, open[instance].Expect!.Value);
            }

            DateTime? expectAt = ev.DurationMs >= 0 ? ev.At.AddMilliseconds(ev.DurationMs) : null;
            switch (ev.Kind)
            {
                case BuffKind.On:
                    Close(ev.Instance, ev.At);
                    open[ev.Instance] = (ev.At, ev.SkillId, expectAt);
                    break;
                case BuffKind.Renewed:
                    open[ev.Instance] = open.TryGetValue(ev.Instance, out var current) ? current with { Expect = expectAt } : (ev.At, ev.SkillId, expectAt);
                    break;
                case BuffKind.End:
                    Close(ev.Instance, ev.At);
                    break;
            }
        }

        foreach (int instance in open.Keys.ToList())
        {
            DateTime stop = open[instance].Expect is DateTime expect && expect < run.End ? expect : run.End;
            Close(instance, stop);
        }

        return spans.GroupBy(s => Aion2SkillNames.NameOf(s.Item1)).Where(g => g.Sum(s => s.Item3 - s.Item2) >= 0.5).SelectMany(g => g).ToList();
    }
}

/// <summary>The runs kept in a file, so they are still there at the next start.</summary>
public static class DummyStore
{
    public static string DefaultPath => Path.Combine(Ui.MeterSettings.DataDirectory, "dummy-test.json");

    private sealed record EventDto(long T, int S, int G, long A, string? Sk, bool C, bool Tk, int Id, int N);

    private sealed record UseDto(long T, int A, int Id);

    private sealed record BuffDto(long T, int K, int G, int I, int Id, int C, int D);

    private sealed record RunDto(int Number, long Start, double PlannedSeconds, double LengthSeconds, string Note, int PlayerId,
        List<EventDto> Events, List<UseDto> Uses, List<BuffDto> Buffs, Dictionary<string, string> Names);

    public static void Save(string path, IEnumerable<DummyRun> runs)
    {
        var dtos = runs.Where(r => r.Closed).Select(r => new RunDto(r.Number, r.Start.Ticks, r.Planned.TotalSeconds, r.Length.TotalSeconds, r.Note, r.PlayerId,
            r.Events.Select(e => new EventDto(e.Timestamp.Ticks, e.SourceObjectId, e.TargetObjectId, e.Amount, e.Skill, e.IsCritical, e.IsTick, e.SkillId, e.HitCount)).ToList(),
            r.Uses.Select(u => new UseDto(u.At.Ticks, u.Actor, u.SkillId)).ToList(),
            r.Buffs.Select(b => new BuffDto(b.At.Ticks, (int)b.Kind, b.Target, b.Instance, b.SkillId, b.Caster, b.DurationMs)).ToList(),
            r.Names.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value))).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(dtos));
        File.Move(temp, path, overwrite: true);
    }

    public static List<DummyRun> Load(string path)
    {
        var runs = new List<DummyRun>();
        try
        {
            if (!File.Exists(path))
            {
                return runs;
            }

            foreach (RunDto d in JsonSerializer.Deserialize<List<RunDto>>(File.ReadAllText(path)) ?? new())
            {
                var run = new DummyRun
                {
                    Number = d.Number,
                    Start = new DateTime(d.Start),
                    Planned = TimeSpan.FromSeconds(d.PlannedSeconds),
                    Length = TimeSpan.FromSeconds(d.LengthSeconds),
                    Note = d.Note,
                    PlayerId = d.PlayerId,
                    Closed = true,
                };
                run.Events.AddRange(d.Events.Select(e => new DamageEvent(new DateTime(e.T), e.S, e.G, e.A, false, e.Sk, e.C, e.Tk, e.Id, null, e.N)));
                run.Uses.AddRange(d.Uses.Select(u => new SkillUse(new DateTime(u.T), u.A, u.Id)));
                run.Buffs.AddRange(d.Buffs.Select(b => new BuffEvent(new DateTime(b.T), (BuffKind)b.K, b.G, b.I, b.Id, b.C, b.D)));
                foreach (var (key, value) in d.Names)
                {
                    run.Names[int.Parse(key)] = value;
                }

                runs.Add(run);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
        {
            // A damaged file starts the list empty; the next save writes a new one.
        }

        return runs;
    }
}
