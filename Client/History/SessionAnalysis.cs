using AionDPS.Combat;

namespace AionDPS.History;

public enum FindingLevel
{
    /// <summary>Checked and in order.</summary>
    Ok,
    /// <summary>Worth knowing, no reason to doubt the numbers.</summary>
    Info,
    /// <summary>Unusual: the numbers may be incomplete or oddly attributed.</summary>
    Warn,
    /// <summary>Cannot be right: more damage counted than the target had.</summary>
    Bad,
}

/// <summary>One line of the check. <see cref="Key"/> is a text key of the interface strings; <see cref="Args"/> fill its {0}, {1} ...</summary>
public sealed record SessionFinding(FindingLevel Level, string Key, object[] Args);

/// <summary>A boss gone in with a boss: one attempt (a wipe and a retry are two).</summary>
/// <param name="Enrage">When the boss started hitting much harder and kept doing so, if it did.</param>
public sealed record BossAttempt(int TargetId, string Name, DateTime Start, DateTime End, long Counted, bool Killed, HpCheckResult? Check, EnrageInfo? Enrage)
{
    public TimeSpan Duration => End - Start;
}

/// <param name="At">Time into the attempt.</param>
/// <param name="Factor">How many times the boss's damage per second before it; 0 when it cannot be told.</param>
/// <param name="Exact">The server announced it (see EnrageMark), as against read from the damage.</param>
public sealed record EnrageInfo(TimeSpan At, double Factor, bool Exact = false);

public sealed record SessionAnalysis(IReadOnlyList<BossAttempt> Attempts, IReadOnlyList<SessionFinding> Findings)
{
    /// <summary>The worst thing found; Info when there was nothing to check (no boss, no hit points).</summary>
    public FindingLevel Overall => Findings.Count == 0 ? FindingLevel.Info : Findings.Max(f => f.Level);

    public bool HasChecks => Findings.Any(f => f.Level is FindingLevel.Ok or FindingLevel.Warn or FindingLevel.Bad);

    public bool HasEnrage => Attempts.Any(a => a.Enrage is not null);
}

/// <summary>
/// The plausibility check of a stay, for the PvE case that someone's totals cannot be trusted - a log edited so that one player owns all
/// the damage, or damage counted twice. The strongest guard is the server's own hit points: what the boss lost must be what was counted
/// (see <see cref="HpCheck"/>). Where those are missing the check can only look at the shape of the numbers (one player with nearly all of
/// it, hits repeated at the same instant). It is a sanity check on a file, not proof: whoever edits the events can edit the hit points too.
/// </summary>
public static class SessionAnalyzer
{
    /// <summary>The share of a boss's damage above which one player looks like the only one, when others were there.</summary>
    public const double OnePlayerShare = 0.97;

    /// <summary>The share of hits seen twice at the same instant above which the numbers look doubled.</summary>
    public const double DuplicateShare = 0.05;

    /// <summary>Shorter fights are not checked: training dummies and trash die in seconds, and a few hits either side of an HP reading
    /// are enough to make the ratio swing (recordings of 2026-10-06 to 10-11: nothing real was under half a minute).</summary>
    public static readonly TimeSpan MinAttempt = TimeSpan.FromSeconds(30);

    public static SessionAnalysis Analyze(SessionInfo session)
    {
        // A finished stay is analysed once; the open one again when it has grown.
        int count = session.Data.Events.Count;
        if (session.CachedAnalysis is { } cached && cached.EventCount == count && !session.IsOpen)
        {
            return cached.Result;
        }

        SessionAnalysis result = Compute(session);
        session.CachedAnalysis = (count, result);
        return result;
    }

    private static SessionAnalysis Compute(SessionInfo session)
    {
        var findings = new List<SessionFinding>();
        var attempts = new List<BossAttempt>();
        IReadOnlyList<DamageEvent> events = session.Data.Events;
        bool classesKnown = session.Classes.Count > 0;

        foreach (int targetId in SessionRecorder.BossCandidates(session))
        {
            string name = session.Names.GetValueOrDefault(targetId) ?? $"0x{targetId:X8}";
            session.Hp.TryGetValue(targetId, out TargetHp? hp);
            IReadOnlyList<DateTime> resets = hp?.Resets ?? Array.Empty<DateTime>();

            int attemptNo = 0;
            foreach (FightSegment fight in FightSegmenter.Segment(events, targetId))
            {
                foreach ((DateTime from, DateTime to, List<DamageEvent> hits) in SplitAtResets(fight, resets))
                {
                    long counted = hits.Sum(h => h.Amount);
                    if (counted < SessionRecorder.BossMinDamage || to - from < MinAttempt)
                    {
                        continue;
                    }

                    attemptNo++;
                    HpCheckResult? check = hp is null
                        ? null
                        : HpCheck.Evaluate(
                            hp.Samples.Where(s => s.At >= from.AddSeconds(-1) && s.At <= to.AddSeconds(1)).Select(s => (s.At, s.Hp)).ToList(),
                            hits.Concat(OwnHeals(events, targetId, from, to)).ToList(),
                            hp.Highest);
                    bool killed = check?.Killed ?? hp?.Samples.Any(s => s.At >= to && s.Hp == 0) == true;
                    IEnumerable<DamageEvent> bossOutput = events.Where(e => !e.IsHeal && e.SourceObjectId == targetId);
                    // The server's own mark wins; without one (the meter came in mid-fight) the damage is read.
                    EnrageInfo? enrage = session.Enrages.FirstOrDefault(m => m.EntityId == targetId && m.Timestamp >= from.AddSeconds(-1) && m.Timestamp <= to.AddSeconds(5)) is { EntityId: > 0 } mark
                        ? new EnrageInfo(mark.Timestamp - mark.EngagedAt, BossEnrage.FactorAfter(bossOutput, from, to, mark.Timestamp), Exact: true)
                        : BossEnrage.Detect(bossOutput, from, to);
                    attempts.Add(new BossAttempt(targetId, name, from, to, counted, killed, check, enrage));
                    string label = attemptNo > 1 || resets.Count > 0 ? $"{name} #{attemptNo}" : name;

                    if (check is null)
                    {
                        findings.Add(new SessionFinding(FindingLevel.Info, "Sessions.Check.NoHp", new object[] { label }));
                    }
                    else
                    {
                        findings.Add(HpFinding(label, check));
                    }

                    if (OneOwner(hits, session, classesKnown) is (string owner, double share))
                    {
                        findings.Add(new SessionFinding(FindingLevel.Warn, "Sessions.Check.OnePlayer", new object[] { label, owner, share.ToString("P0") }));
                    }
                }
            }
        }

        double doubled = DuplicateRatio(events);
        if (doubled >= DuplicateShare)
        {
            findings.Add(new SessionFinding(FindingLevel.Warn, "Sessions.Check.Duplicates", new object[] { doubled.ToString("P0") }));
        }

        return new SessionAnalysis(attempts, findings);
    }

    private static SessionFinding HpFinding(string label, HpCheckResult check)
    {
        string percent = check.Ratio.ToString("P0");
        if (check.OverFullHealth)
        {
            return new SessionFinding(FindingLevel.Bad, "Sessions.Check.OverFull", new object[] { label, check.RunTotal.ToString("N0"), check.Highest.ToString("N0") });
        }

        return check.Verdict switch
        {
            HpCheckVerdict.Excess => new SessionFinding(FindingLevel.Bad, "Sessions.Check.HpExcess", new object[] { label, percent, check.Lost.ToString("N0") }),
            // Less than the server says is no sign of inflated numbers: hits were not seen (packets lost, a big raid of others), and elite targets lose about
            // 4.5 times their shown damage (a constant 22 % in the recordings). Said, but it does not mark the session.
            HpCheckVerdict.Missing => new SessionFinding(FindingLevel.Info, "Sessions.Check.HpMissing", new object[] { label, percent, check.Lost.ToString("N0") }),
            _ => new SessionFinding(FindingLevel.Ok, "Sessions.Check.HpOk", new object[] { label, percent, check.Lost.ToString("N0") }),
        };
    }

    /// <summary>A boss that came back to full health under the same id is a second attempt: the fight is cut at each such moment.</summary>
    private static IEnumerable<(DateTime From, DateTime To, List<DamageEvent> Hits)> SplitAtResets(FightSegment fight, IReadOnlyList<DateTime> resets)
    {
        var cuts = resets.Where(r => r > fight.Start && r < fight.End).OrderBy(r => r).ToList();
        DateTime from = fight.Start;
        foreach (DateTime cut in cuts)
        {
            var part = fight.Hits.Where(h => h.Timestamp >= from && h.Timestamp < cut).ToList();
            if (part.Count > 0)
            {
                yield return (part[0].Timestamp, part[^1].Timestamp, part);
            }

            from = cut;
        }

        var rest = fight.Hits.Where(h => h.Timestamp >= from).ToList();
        if (rest.Count > 0)
        {
            yield return (rest[0].Timestamp, rest[^1].Timestamp, rest);
        }
    }

    private static IEnumerable<DamageEvent> OwnHeals(IReadOnlyList<DamageEvent> events, int targetId, DateTime from, DateTime to) =>
        events.Where(e => e.IsHeal && e.SourceObjectId == targetId && e.TargetObjectId == targetId && e.Timestamp >= from && e.Timestamp <= to);

    /// <summary>The player with nearly all of a boss's damage, when other players were in the stay too.</summary>
    private static (string Name, double Share)? OneOwner(List<DamageEvent> hits, SessionInfo session, bool classesKnown)
    {
        long total = hits.Sum(h => h.Amount);
        if (total < SessionRecorder.BossMinDamage * 2)
        {
            return null;
        }

        var players = session.Data.Events
            .Select(e => e.SourceObjectId)
            .Where(id => !classesKnown || session.Classes.ContainsKey(id))
            .Distinct()
            .Count();
        if (players < 2)
        {
            return null;
        }

        var top = hits.Where(h => !classesKnown || session.Classes.ContainsKey(h.SourceObjectId))
            .GroupBy(h => h.SourceObjectId)
            .Select(g => (Id: g.Key, Damage: g.Sum(h => h.Amount)))
            .OrderByDescending(x => x.Damage)
            .FirstOrDefault();
        double share = top.Damage / (double)total;
        return share >= OnePlayerShare ? (session.Names.GetValueOrDefault(top.Id) ?? $"0x{top.Id:X8}", share) : null;
    }

    /// <summary>The share of hits that have an exact twin: same instant, source, target, amount and skill.</summary>
    public static double DuplicateRatio(IReadOnlyList<DamageEvent> events)
    {
        if (events.Count < 200)
        {
            return 0;
        }

        var seen = new HashSet<(long, int, int, long, string?)>();
        int twins = 0;
        foreach (DamageEvent e in events)
        {
            if (!seen.Add((e.Timestamp.Ticks, e.SourceObjectId, e.TargetObjectId, e.Amount, e.Skill)))
            {
                twins++;
            }
        }

        return twins / (double)events.Count;
    }
}

/// <summary>
/// The boss's rage: after a while a boss hits much harder and keeps doing so until it dies or the group does. The time is not known, so it is
/// read from the fight: the boss's damage per second in bins of <see cref="Bin"/>, compared with the first half of the fight.
/// A surge that ends again is a mechanic, not a rage.
/// </summary>
public static class BossEnrage
{
    public static readonly TimeSpan Bin = TimeSpan.FromSeconds(5);

    /// <summary>The shortest fight the rage is looked for in.</summary>
    public static readonly TimeSpan MinFight = TimeSpan.FromSeconds(60);

    /// <summary>The rage is not looked for before this far into the fight. It seems to come at about five minutes (the two Talisra fights of
    /// 2026-10-07 and 10-09 that ran past it show the boss's damage at its highest in the last minute); a jump earlier than this was a phase or the
    /// kill burst - the fights that ended at 3:15 to 3:50 all "raged" in their last seconds.</summary>
    public static readonly TimeSpan Earliest = TimeSpan.FromMinutes(4);

    /// <summary>The bin must reach this many times the normal output...</summary>
    public const double Rise = 1.8;

    /// <summary>...and every bin after it must keep at least this many times.</summary>
    public const double Sustain = 1.5;

    /// <summary>How many times the boss's damage per second rose at <paramref name="at"/>: the mean of the 5 s bins after it against the median of the
    /// bins with any output before it. 0 when there are too few bins to tell.</summary>
    public static double FactorAfter(IEnumerable<DamageEvent> bossOutput, DateTime start, DateTime end, DateTime at)
    {
        int bins = (int)Math.Ceiling((end - start) / Bin);
        if (bins < 2 || at <= start)
        {
            return 0;
        }

        var damage = new double[bins];
        foreach (DamageEvent e in bossOutput)
        {
            if (e.Timestamp >= start && e.Timestamp <= end)
            {
                damage[Math.Min(bins - 1, (int)((e.Timestamp - start) / Bin))] += e.Amount;
            }
        }

        int split = Math.Min(bins - 1, (int)((at - start) / Bin));
        var before = damage.Take(split).Where(d => d > 0).OrderBy(d => d).ToList();
        double[] after = damage.Skip(split).ToArray();
        return before.Count < 6 || after.Length < 2 || after.Average() <= 0 ? 0 : Math.Round(after.Average() / before[before.Count / 2], 1);
    }

    public static EnrageInfo? Detect(IEnumerable<DamageEvent> bossOutput, DateTime start, DateTime end)
    {
        TimeSpan length = end - start;
        if (length < MinFight)
        {
            return null;
        }

        int bins = (int)Math.Ceiling(length / Bin);
        var damage = new double[bins];
        foreach (DamageEvent e in bossOutput)
        {
            if (e.Timestamp >= start && e.Timestamp <= end)
            {
                damage[Math.Min(bins - 1, (int)((e.Timestamp - start) / Bin))] += e.Amount;
            }
        }

        // What is normal: the bins of the first half that have any output (a boss that swings now and then has quiet bins).
        var early = damage.Take(bins / 2).Where(d => d > 0).OrderBy(d => d).ToList();
        if (early.Count < 6)
        {
            return null;
        }

        double normal = early[early.Count / 2];
        for (int i = Math.Max(bins / 2 - 1, (int)(Earliest / Bin)); i < bins - 3; i++)
        {
            if (damage[i] < normal * Rise)
            {
                continue;
            }

            // At most one weak bin after the rise (the boss turning, a knock-down); then it has to stay up to the end.
            int weak = 0;
            for (int j = i; j < bins; j++)
            {
                if (damage[j] < normal * Sustain && ++weak > 1)
                {
                    break;
                }
            }

            if (weak <= 1)
            {
                double factor = damage.Skip(i).Average() / normal;
                return new EnrageInfo(i * Bin, Math.Round(factor, 1));
            }
        }

        return null;
    }
}
