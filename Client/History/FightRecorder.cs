using AionDPS.Combat;

namespace AionDPS.History;

/// <summary>What the recorder needs from the meter to describe a fight's participants - the same
/// name/class/side resolution MainWindow uses for its rows, handed in as callbacks so this class
/// stays free of UI types.</summary>
public sealed record FightContext(
    Func<int, string> NameOf,
    Func<int, string> ClassOf,
    Func<int, string> FactionOf,
    Func<int, bool> IsPlayer,
    Func<int, bool> IsSelf,
    Func<int, bool> IsEnemy,
    Func<string, bool> IsIgnoredTarget,
    string Game,
    string? ServerName);

/// <summary>
/// Watches the live event list and files each fight into the <see cref="FightStore"/> once it is
/// over - "over" being <see cref="IdleGap"/> of silence on that target, the same gap that
/// separates two runs of one boss elsewhere in the meter. Fights shorter than
/// <see cref="MinDuration"/>, training dummies and trash mobs are not worth a history entry.
/// Each (target, start) pair is recorded once; Clear/exit flush whatever is still open.
/// </summary>
public sealed class FightRecorder
{
    private readonly FightStore _store;
    private readonly HashSet<(int TargetId, DateTime Start)> _recorded = new();

    public FightRecorder(FightStore store)
    {
        _store = store;
    }

    public TimeSpan IdleGap { get; init; } = TimeSpan.FromSeconds(FightSegmenter.DefaultGapSeconds);

    public TimeSpan MinDuration { get; init; } = TimeSpan.FromSeconds(10);

    public int Recorded { get; private set; }

    /// <summary>Records every fight that has ended by <paramref name="now"/> (or every open one
    /// when <paramref name="flushAll"/>). Returns how many were written this call.</summary>
    public int Tick(IReadOnlyList<DamageEvent> events, DateTime now, FightContext context, bool flushAll = false)
    {
        int written = 0;
        var targetIds = events.Where(ev => !ev.IsHeal).Select(ev => ev.TargetObjectId).Distinct().ToList();
        foreach (int targetId in targetIds)
        {
            if (context.IsPlayer(targetId))
            {
                continue;
            }

            string targetName = context.NameOf(targetId);
            if (context.IsIgnoredTarget(targetName))
            {
                continue;
            }

            foreach (FightSegment segment in FightSegmenter.Segment(events, targetId, IdleGap.TotalSeconds))
            {
                bool ended = flushAll || now - segment.End > IdleGap;
                if (!ended || segment.Duration < MinDuration || _recorded.Contains((targetId, segment.Start)))
                {
                    continue;
                }

                _store.Insert(Describe(segment, targetName, events, context));
                _recorded.Add((targetId, segment.Start));
                Recorded++;
                written++;
            }
        }

        return written;
    }

    /// <summary>After the meter's own Clear: the events are gone, so nothing must be remembered
    /// as "already recorded" for ids the next session will reuse.</summary>
    public void Reset() => _recorded.Clear();

    private static FightDetail Describe(FightSegment segment, string targetName, IReadOnlyList<DamageEvent> allEvents, FightContext context)
    {
        // Everything inside the fight's own window, heals included, is what a replay needs.
        var window = allEvents
            .Where(ev => ev.Timestamp >= segment.Start && ev.Timestamp <= segment.End)
            .OrderBy(ev => ev.Timestamp)
            .ToList();

        var participantIds = segment.Hits.Select(h => h.SourceObjectId)
            .Concat(window.Where(ev => ev.IsHeal).Select(ev => ev.SourceObjectId))
            .Distinct()
            .Where(context.IsPlayer)
            .ToList();

        var participants = new List<FightParticipant>();
        foreach (int id in participantIds)
        {
            var hitsOnTarget = segment.Hits.Where(h => h.SourceObjectId == id).ToList();
            participants.Add(new FightParticipant(
                context.NameOf(id),
                context.ClassOf(id),
                context.FactionOf(id),
                context.IsSelf(id),
                context.IsEnemy(id),
                hitsOnTarget.Sum(h => h.Amount),
                DpsCalculator.TargetIDps(segment.Hits, segment.TargetId, id),
                window.Where(ev => ev.IsHeal && ev.SourceObjectId == id).Sum(ev => ev.Amount),
                window.Where(ev => !ev.IsHeal && ev.SourceObjectId == segment.TargetId && ev.TargetObjectId == id).Sum(ev => ev.Amount)));
        }

        participants.Sort((a, b) => b.Damage.CompareTo(a.Damage));

        var names = window.SelectMany(ev => new[] { ev.SourceObjectId, ev.TargetObjectId })
            .Distinct()
            .ToDictionary(id => id, context.NameOf);

        var summary = new FightSummary(
            0,
            context.Game,
            context.ServerName,
            segment.Start,
            segment.End,
            targetName,
            "pve",
            segment.Hits.Sum(h => h.Amount),
            participants.Count,
            participants.FirstOrDefault(p => p.IsSelf)?.Name);
        return new FightDetail(summary, participants, window, names);
    }
}
