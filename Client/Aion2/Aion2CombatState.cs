using System.IO;
using System.Text.Json;
using AionDPS.Combat;

namespace AionDPS.Aion2;

/// <summary>A monster's (or a player's) skill that has begun: the game's "windup" frame. It names the caster, the
/// skill, whom it is aimed at and how long the cast takes; the damage follows a moment after the cast has ended.</summary>
public readonly record struct CastNotice(
    int Actor, int SkillId, int Target, DateTime Start, int DurationMs, double Facing, float X, float Y, float Z, bool HasPosition)
{
    public DateTime End => Start.AddMilliseconds(DurationMs);
}

/// <summary>An effect put on an entity (the game's buff frames). <see cref="DurationMs"/> is -1 for one that stays until it is taken off.</summary>
public readonly record struct BuffNotice(DateTime At, int Target, int Instance, int EffectId, int SkillId, int DurationMs, int Caster)
{
    public DateTime? Ends => DurationMs < 0 ? null : At.AddMilliseconds(DurationMs);
}

/// <summary>What the meter has learnt about one skill from watching it: how often it hit more than its target (an area),
/// when its damage lands after the cast has ended, and the highest hit it did.</summary>
public sealed class SkillLesson
{
    public int Seen { get; set; }

    /// <summary>Uses that hit more than one player, or someone other than the target.</summary>
    public int Wide { get; set; }

    /// <summary>Uses that hit only the one it was aimed at.</summary>
    public int Single { get; set; }

    /// <summary>The last delays (ms) between the end of the cast and the first hit on a player.</summary>
    public List<int> Land { get; set; } = new();

    /// <summary>The highest hit on the local player / on any player.</summary>
    public long MaxOnYou { get; set; }

    public long MaxAny { get; set; }

    public int CastMs { get; set; }

    /// <summary>An area when it has hit several players, in at least a quarter of its uses.</summary>
    public bool IsArea => Wide >= 1 && Wide * 4 >= Single;

    /// <summary>The delay from the end of the cast to the damage, once at least three observations agree within 200 ms
    /// (the median of them); null until then - a guess would mislead a dodge.</summary>
    public int? LandMs
    {
        get
        {
            if (Land.Count < 3)
            {
                return null;
            }

            int min = Land.Min();
            int max = Land.Max();
            if (max - min > 200)
            {
                return null;
            }

            int median = Land.OrderBy(v => v).ElementAt(Land.Count / 2);
            return median >= 150 ? median : null;
        }
    }
}

/// <summary>A cast as the overlay shows it.</summary>
public sealed record CastView(CastNotice Notice, bool IsArea, int? LandMs, long MaxOnYou, long MaxAny, bool Stopped);

/// <summary>
/// What the packet stream says about fights beyond damage: skills that are being cast (and how long), whom an entity last
/// aimed at, and the effects on each entity. Fed from the capture thread by <see cref="Protocol.Aion2FrameDecoder"/>, read by
/// the overlays on the UI thread, so everything sits behind one lock.
/// </summary>
public sealed class Aion2CombatState
{
    /// <summary>A cast stays known this long after it ended (its damage lands, the area is learnt).</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(4);

    private const int MaxActive = 200;
    private const int MaxLessons = 250;
    private const int MaxLandSamples = 6;
    private const int MaxBuffsPerTarget = 80;
    private const int MaxTargets = 3000;

    private readonly object _gate = new();
    private readonly List<ActiveCast> _active = new();
    private readonly Dictionary<int, SkillLesson> _lessons = new();
    private readonly Dictionary<int, (int Target, DateTime At)> _lastAim = new();
    private readonly Dictionary<int, Dictionary<int, BuffNotice>> _buffs = new();
    private bool _dirty;
    private readonly List<SkillUse> _uses = new();
    private readonly List<BuffEvent> _buffLog = new();
    private readonly List<StaggerEvent> _staggerLog = new();
    private const int MaxLog = 200_000;

    /// <summary>Where the learnt skills are kept between sessions; null keeps them in memory only (replays, pictures, tests).</summary>
    public string? FilePath { get; set; }

    private sealed class ActiveCast
    {
        public ActiveCast(CastNotice notice) => Notice = notice;

        public CastNotice Notice { get; }
        public HashSet<int> Victims { get; } = new();
        public bool Landed;
        public bool Stopped;
        public bool Finished;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _active.Clear();
            _lastAim.Clear();
            _buffs.Clear();
        }
    }

    // ---- casts -----------------------------------------------------------------------------------------------

    public void NoteWindup(CastNotice cast)
    {
        lock (_gate)
        {
            Prune(cast.Start);
            _active.RemoveAll(a => a.Notice.Actor == cast.Actor && a.Notice.SkillId == cast.SkillId && a.Notice.Start == cast.Start);
            if (_active.Count >= MaxActive)
            {
                _active.RemoveAt(0);
            }

            _active.Add(new ActiveCast(cast));
            _lastAim[cast.Actor] = (cast.Target, cast.Start);
            Lesson(cast.SkillId).CastMs = cast.DurationMs;
        }
    }

    /// <summary>A skill used by <paramref name="actor"/> on <paramref name="target"/> (a cast notice or a hit): remembered as what
    /// the actor is aiming at, which is what "the target of the target" reads.</summary>
    public void NoteAim(int actor, int target, DateTime at)
    {
        if (target <= 0 || actor <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _lastAim[actor] = (target, at);
            if (_lastAim.Count > 6000)
            {
                foreach (int key in _lastAim.Where(kv => at - kv.Value.At > TimeSpan.FromMinutes(5)).Select(kv => kv.Key).ToList())
                {
                    _lastAim.Remove(key);
                }
            }
        }
    }

    /// <summary>The one an entity last aimed a skill at, when that was no longer ago than <paramref name="maxAge"/>.</summary>
    public int? TargetOf(int entity, DateTime now, TimeSpan maxAge)
    {
        lock (_gate)
        {
            return _lastAim.TryGetValue(entity, out var aim) && now - aim.At <= maxAge && aim.At <= now + TimeSpan.FromSeconds(2) ? aim.Target : null;
        }
    }

    /// <summary>A hit with damage: closes in on which casts it belongs to and what the skill does.</summary>
    public void NoteHit(DateTime at, int actor, int target, int skillId, long amount, bool targetIsPlayer, bool targetIsLocal)
    {
        lock (_gate)
        {
            SkillLesson? lesson = null;
            if (targetIsPlayer)
            {
                lesson = Lesson(skillId);
                lesson.MaxAny = Math.Max(lesson.MaxAny, amount);
                if (targetIsLocal)
                {
                    lesson.MaxOnYou = Math.Max(lesson.MaxOnYou, amount);
                }

                _dirty = true;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveCast cast = _active[i];
                if (cast.Notice.Actor != actor || cast.Notice.SkillId != skillId)
                {
                    continue;
                }

                if (at < cast.Notice.Start || at > cast.Notice.End + Linger)
                {
                    continue;
                }

                if (targetIsPlayer)
                {
                    cast.Victims.Add(target);
                    if (!cast.Landed && at >= cast.Notice.End - TimeSpan.FromMilliseconds(100))
                    {
                        cast.Landed = true;
                        int delay = (int)(at - cast.Notice.End).TotalMilliseconds;
                        if (delay is >= 0 and <= 3500)
                        {
                            SkillLesson l = lesson ?? Lesson(skillId);
                            l.Land.Add(delay);
                            if (l.Land.Count > MaxLandSamples)
                            {
                                l.Land.RemoveAt(0);
                            }
                        }
                    }
                }

                break;
            }
        }
    }

    /// <summary>The entity died or left: its casts are over ("stopped"), they do not run on to the end.</summary>
    public void NoteGone(int entity, DateTime at)
    {
        lock (_gate)
        {
            foreach (ActiveCast cast in _active.Where(c => c.Notice.Actor == entity && !c.Finished && at < c.Notice.End))
            {
                cast.Stopped = true;
            }

            _lastAim.Remove(entity);
        }
    }

    /// <summary>The casts that are running at <paramref name="now"/> (not yet ended, or ended for less than <paramref name="grace"/>), soonest to end first.</summary>
    public IReadOnlyList<CastView> Casts(DateTime now, TimeSpan grace)
    {
        lock (_gate)
        {
            Prune(now);
            var list = new List<CastView>();
            foreach (ActiveCast cast in _active)
            {
                if (cast.Stopped || now < cast.Notice.Start || now > cast.Notice.End + grace)
                {
                    continue;
                }

                SkillLesson? lesson = _lessons.GetValueOrDefault(cast.Notice.SkillId);
                bool aimedAtSelf = cast.Notice.Target == cast.Notice.Actor || cast.Notice.Target <= 0;
                list.Add(new CastView(cast.Notice, aimedAtSelf || (lesson?.IsArea ?? false), lesson?.LandMs, lesson?.MaxOnYou ?? 0, lesson?.MaxAny ?? 0, cast.Stopped));
            }

            return list.OrderBy(c => c.Notice.End).ToList();
        }
    }

    /// <summary>Every skill learnt, most seen first (diagnostics and the replay command).</summary>
    public IReadOnlyList<(int SkillId, SkillLesson Lesson)> Lessons()
    {
        lock (_gate)
        {
            Prune(DateTime.Now);
            return _lessons.OrderByDescending(kv => kv.Value.Seen).Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }

    public SkillLesson? LessonOf(int skillId)
    {
        lock (_gate)
        {
            return _lessons.GetValueOrDefault(skillId);
        }
    }

    private SkillLesson Lesson(int skillId)
    {
        if (!_lessons.TryGetValue(skillId, out SkillLesson? lesson))
        {
            lesson = new SkillLesson();
            _lessons[skillId] = lesson;
            if (_lessons.Count > MaxLessons)
            {
                int drop = _lessons.OrderBy(kv => kv.Value.Seen).First().Key;
                _lessons.Remove(drop);
            }
        }

        return lesson;
    }

    /// <summary>Finishes the casts that are over: counts who they hit (an area or not) and drops the old ones.</summary>
    private void Prune(DateTime now)
    {
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            ActiveCast cast = _active[i];
            if (cast.Finished || now <= cast.Notice.End + Linger)
            {
                if (cast.Finished && now > cast.Notice.End + Linger + TimeSpan.FromSeconds(30))
                {
                    _active.RemoveAt(i);
                }

                continue;
            }

            cast.Finished = true;
            if (cast.Stopped)
            {
                continue;
            }

            SkillLesson lesson = Lesson(cast.Notice.SkillId);
            lesson.Seen++;
            if (cast.Victims.Count >= 2 || (cast.Victims.Count == 1 && cast.Notice.Target > 0 && !cast.Victims.Contains(cast.Notice.Target)))
            {
                lesson.Wide++;
            }
            else if (cast.Victims.Count == 1)
            {
                lesson.Single++;
            }

            _dirty = true;
        }
    }

    // ---- the log of a fight: what the rotation and buff views read -----------------------------------------------

    /// <summary>A skill a player used (the cast notice).</summary>
    public void NoteSkillUse(DateTime at, int actor, int skillId)
    {
        lock (_gate)
        {
            Append(_uses, new SkillUse(at, actor, skillId));
        }
    }

    public void NoteStagger(StaggerEvent stagger)
    {
        lock (_gate)
        {
            Append(_staggerLog, stagger);
        }
    }

    private static void Append<T>(List<T> list, T item)
    {
        if (list.Count >= MaxLog)
        {
            list.RemoveRange(0, MaxLog / 4);
        }

        list.Add(item);
    }

    /// <summary>What was logged between two moments. Effects are taken from a few minutes earlier, so one put on before the fight began still has its start.</summary>
    public FightExtras Slice(DateTime from, DateTime to, Func<int, string?> nameOf)
    {
        lock (_gate)
        {
            DateTime buffFrom = from - TimeSpan.FromMinutes(10);
            var uses = _uses.Where(u => u.At >= from && u.At <= to).ToList();
            var buffs = _buffLog.Where(b => b.At >= buffFrom && b.At <= to).ToList();
            var staggers = _staggerLog.Where(s => s.At >= from && s.At <= to).ToList();
            var ids = new HashSet<int>();
            foreach (SkillUse u in uses) { ids.Add(u.Actor); }
            foreach (BuffEvent b in buffs) { ids.Add(b.Target); ids.Add(b.Caster); }
            foreach (StaggerEvent s in staggers) { ids.Add(s.Entity); }
            var names = new Dictionary<int, string>();
            foreach (int id in ids)
            {
                if (nameOf(id) is { Length: > 0 } name)
                {
                    names[id] = name;
                }
            }

            return new FightExtras(uses, buffs, staggers, names);
        }
    }

    // ---- effects ---------------------------------------------------------------------------------------------

    public void NoteBuff(BuffNotice buff, bool renewed = false)
    {
        lock (_gate)
        {
            Append(_buffLog, new BuffEvent(buff.At, renewed ? BuffKind.Renewed : BuffKind.On, buff.Target, buff.Instance, buff.SkillId, buff.Caster, buff.DurationMs));
            if (!_buffs.TryGetValue(buff.Target, out var byInstance))
            {
                if (_buffs.Count >= MaxTargets)
                {
                    _buffs.Remove(_buffs.Keys.First());
                }

                byInstance = new Dictionary<int, BuffNotice>();
                _buffs[buff.Target] = byInstance;
            }

            if (byInstance.Count >= MaxBuffsPerTarget && !byInstance.ContainsKey(buff.Instance))
            {
                int oldest = byInstance.OrderBy(kv => kv.Value.At).First().Key;
                byInstance.Remove(oldest);
            }

            byInstance[buff.Instance] = buff;
        }
    }

    public void NoteBuffEnd(int target, int instance, DateTime? at = null)
    {
        lock (_gate)
        {
            if (at is DateTime when)
            {
                Append(_buffLog, new BuffEvent(when, BuffKind.End, target, instance, 0, 0, 0));
            }

            if (_buffs.TryGetValue(target, out var byInstance))
            {
                byInstance.Remove(instance);
            }
        }
    }

    /// <summary>The effects on an entity that are still running at <paramref name="now"/>, newest first.</summary>
    public IReadOnlyList<BuffNotice> BuffsOn(int target, DateTime now)
    {
        lock (_gate)
        {
            if (!_buffs.TryGetValue(target, out var byInstance))
            {
                return Array.Empty<BuffNotice>();
            }

            return byInstance.Values.Where(b => b.Ends is not { } end || end > now).OrderByDescending(b => b.At).ToList();
        }
    }

    public void ForgetEntity(int entity)
    {
        lock (_gate)
        {
            _buffs.Remove(entity);
            _lastAim.Remove(entity);
        }
    }

    // ---- what is learnt, kept between sessions -----------------------------------------------------------------

    private sealed class FileShape
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, SkillLesson> Skills { get; set; } = new();
    }

    public void Load()
    {
        if (FilePath is not { Length: > 0 } path || !File.Exists(path))
        {
            return;
        }

        try
        {
            FileShape? shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path));
            if (shape is null || shape.Version != 1)
            {
                return;
            }

            lock (_gate)
            {
                foreach (var (key, lesson) in shape.Skills)
                {
                    if (int.TryParse(key, out int id))
                    {
                        _lessons[id] = lesson;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a damaged file only costs what was learnt; the next save writes a new one
        }
    }

    /// <summary>Writes what was learnt when it changed. Called now and then from the UI thread and when the meter closes.</summary>
    public void SaveIfChanged()
    {
        if (FilePath is not { Length: > 0 } path)
        {
            return;
        }

        string json;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            Prune(DateTime.Now);
            var shape = new FileShape();
            foreach (var (id, lesson) in _lessons.Where(kv => kv.Value.Seen > 0 || kv.Value.MaxOnYou > 0).OrderByDescending(kv => kv.Value.Seen).Take(MaxLessons))
            {
                shape.Skills[id.ToString()] = lesson;
            }

            json = JsonSerializer.Serialize(shape);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not worth a message: the lessons are relearnt
        }
    }
}
