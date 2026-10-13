using AionDPS.Aion2.Protocol;

namespace AionDPS.Combat;

/// <summary>One use of a skill on the timeline. <see cref="Row"/> is 0 for a skill that follows a pause and one lower for each skill woven in right after the one before it.</summary>
public sealed record RotationUse(double At, int SkillId, string Name, bool Passive, bool Damaging, int Row, int Number, int OfTotal, double GapSeconds);

/// <summary>"Rotation in short": one line per skill that was used.</summary>
public sealed record RotationSkill(int SkillId, string Name, int Uses, double PerMinute, double AvgGapSeconds, double FirstAt, double SharePercent, bool Damaging);

/// <summary>How long an effect was on the player: its share of the fight, the share of it that came from the player themself, and who gave it.</summary>
public sealed record BuffRow(int SkillId, string Name, double UptimePercent, double FromYouPercent, int Applications, string Givers);

/// <summary>A moment a monster was staggered (its stagger bar broke), in seconds from the start of the fight.</summary>
public sealed record StaggerPhase(double Start, double End);

public sealed record RotationReport(
    double DurationSeconds,
    IReadOnlyList<RotationUse> Uses,
    IReadOnlyList<RotationUse> Opener,
    IReadOnlyList<RotationSkill> Skills,
    IReadOnlyList<BuffRow> Buffs,
    IReadOnlyList<StaggerPhase> Staggers)
{
    public bool HasData => Uses.Count > 0 || Buffs.Count > 0;
}

/// <summary>
/// Works the rotation, effect and stagger views out of a fight's <see cref="FightExtras"/> for one player. Pure: no windows, no packets, so a test can run it.
/// </summary>
public static class RotationAnalysis
{
    /// <summary>A skill used within this long of the one before it is "woven in" and sits one row lower.</summary>
    public const double WeaveSeconds = 0.4;

    /// <summary>A stagger lasts this long when the game says nothing more about it.</summary>
    private const double StaggerSeconds = 5;

    private const int OpenerLength = 8;
    private const int MaxRows = 4;

    public static RotationReport Build(FightExtras extras, int playerId, DateTime start, DateTime end, IReadOnlyList<DamageEvent> playerDamage,
        Func<int, string?> nameOf, Func<int, bool>? isPassive = null)
    {
        double duration = Math.Max(1, (end - start).TotalSeconds);
        isPassive ??= Aion2Artwork.IsPassive;

        // damage per skill name: which skills hit, and their share
        var damageBySkill = playerDamage.Where(e => !e.IsHeal && e.Skill is not null).GroupBy(e => e.Skill!).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
        long damageTotal = damageBySkill.Values.Sum();

        var used = extras.Uses.Where(u => u.Actor == playerId && u.At >= start && u.At <= end).OrderBy(u => u.At).ToList();
        // one skill has an id per level and specialisation (the last digits): the lines of one name are one skill
        var countBySkill = used.GroupBy(u => Aion2SkillNames.NameOf(u.SkillId)).ToDictionary(g => g.Key, g => g.Count());
        var seenBySkill = new Dictionary<string, int>();
        var uses = new List<RotationUse>(used.Count);
        DateTime? previous = null;
        int row = 0;
        foreach (SkillUse u in used)
        {
            string name = Aion2SkillNames.NameOf(u.SkillId);
            double gap = previous is DateTime p ? (u.At - p).TotalSeconds : 0;
            row = previous is not null && gap < WeaveSeconds ? Math.Min(row + 1, MaxRows - 1) : 0;
            previous = u.At;
            seenBySkill[name] = seenBySkill.GetValueOrDefault(name) + 1;
            uses.Add(new RotationUse((u.At - start).TotalSeconds, u.SkillId, name, isPassive(u.SkillId), damageBySkill.ContainsKey(name), row,
                seenBySkill[name], countBySkill[name], gap));
        }

        var skills = new List<RotationSkill>();
        foreach (var group in uses.GroupBy(u => u.Name))
        {
            var list = group.OrderBy(u => u.At).ToList();
            double avgGap = list.Count > 1 ? (list[^1].At - list[0].At) / (list.Count - 1) : 0;
            string name = list[0].Name;
            double share = damageTotal > 0 && damageBySkill.TryGetValue(name, out long dealt) ? 100.0 * dealt / damageTotal : 0;
            skills.Add(new RotationSkill(list[0].SkillId, Aion2SkillNames.Display(name), list.Count, list.Count / (duration / 60.0), avgGap, list[0].At, share, list[0].Damaging));
        }

        skills = skills.OrderByDescending(s => s.SharePercent).ThenByDescending(s => s.Uses).ToList();
        var opener = uses.Where(u => !u.Passive).Take(OpenerLength).ToList();
        return new RotationReport(duration, uses, opener, skills, Buffs(extras, playerId, start, end, duration, nameOf), Staggers(extras, start, end));
    }

    private readonly record struct Span(int SkillId, int Caster, double Start, double End);

    private static IReadOnlyList<BuffRow> Buffs(FightExtras extras, int playerId, DateTime start, DateTime end, double duration, Func<int, string?> nameOf)
    {
        var spans = new List<Span>();
        var open = new Dictionary<int, (DateTime Start, int Skill, int Caster, DateTime? Expect)>();
        var applications = new Dictionary<int, int>();

        void Close(int instance, DateTime at)
        {
            if (open.Remove(instance, out var o))
            {
                double s = Math.Max(0, (o.Start - start).TotalSeconds), e = Math.Min(duration, (at - start).TotalSeconds);
                if (e > s)
                {
                    spans.Add(new Span(o.Skill, o.Caster, s, e));
                }
            }
        }

        foreach (BuffEvent ev in extras.Buffs.Where(b => b.Target == playerId).OrderBy(b => b.At))
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
                    open[ev.Instance] = (ev.At, ev.SkillId, ev.Caster, expectAt);
                    applications[ev.SkillId] = applications.GetValueOrDefault(ev.SkillId) + 1;
                    break;
                case BuffKind.Renewed:
                    if (open.TryGetValue(ev.Instance, out var current))
                    {
                        open[ev.Instance] = current with { Expect = expectAt };
                    }
                    else
                    {
                        open[ev.Instance] = (ev.At, ev.SkillId, ev.Caster, expectAt);
                        applications[ev.SkillId] = applications.GetValueOrDefault(ev.SkillId) + 1;
                    }

                    break;
                case BuffKind.End:
                    Close(ev.Instance, ev.At);
                    break;
            }
        }

        foreach (int instance in open.Keys.ToList())
        {
            DateTime stop = open[instance].Expect is DateTime expect && expect < end ? expect : end;
            Close(instance, stop);
        }

        var rows = new List<BuffRow>();
        foreach (var group in spans.GroupBy(s => Aion2SkillNames.NameOf(s.SkillId)))
        {
            int skillId = group.First().SkillId;
            double all = Union(group.Select(s => (s.Start, s.End)));
            double mine = Union(group.Where(s => s.Caster == playerId).Select(s => (s.Start, s.End)));
            var givers = group.Select(s => s.Caster).Distinct()
                .Select(id => id == playerId ? null : (nameOf(id) ?? extras.Names.GetValueOrDefault(id)))
                .Where(n => !string.IsNullOrEmpty(n) && n != "?").Distinct().Take(3).ToList();
            bool self = group.Any(s => s.Caster == playerId);
            string giverText = string.Join(", ", (self ? new[] { "★" } : Array.Empty<string?>()).Concat(givers));
            rows.Add(new BuffRow(skillId, Aion2SkillNames.Display(group.Key), 100.0 * all / duration, 100.0 * mine / duration,
                group.Select(s => s.SkillId).Distinct().Sum(id => applications.GetValueOrDefault(id)), giverText));
        }

        return rows.Where(r => r.UptimePercent >= 0.5).OrderByDescending(r => r.UptimePercent).Take(60).ToList();
    }

    /// <summary>The length of the union of intervals - an effect two givers kept up at once counts once.</summary>
    private static double Union(IEnumerable<(double Start, double End)> intervals)
    {
        double total = 0, from = double.NegativeInfinity, to = double.NegativeInfinity;
        foreach (var (s, e) in intervals.OrderBy(i => i.Start))
        {
            if (s > to)
            {
                if (to > from)
                {
                    total += to - from;
                }

                from = s;
                to = e;
            }
            else
            {
                to = Math.Max(to, e);
            }
        }

        return to > from ? total + (to - from) : total;
    }

    private static IReadOnlyList<StaggerPhase> Staggers(FightExtras extras, DateTime start, DateTime end)
    {
        var phases = new List<StaggerPhase>();
        foreach (StaggerEvent s in extras.Staggers.Where(s => s.Broke && s.At >= start && s.At <= end).OrderBy(s => s.At))
        {
            double at = (s.At - start).TotalSeconds;
            phases.Add(new StaggerPhase(at, at + StaggerSeconds));
        }

        return phases;
    }
}
