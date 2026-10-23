using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>
/// The time axis of a fight for the upload (the website's group timeline and "buffs per skill"): when each player used which skill and died, and the boss's
/// health over time. Times are tenths of a second from the start of the uploaded window. Pure, so a test can run it.
/// </summary>
public static class TimelineBuilder
{
    /// <summary>The most skill uses one player carries (the backend takes 4000).</summary>
    public const int MaxUses = 4000;

    /// <summary>The most different skills of one player (the backend takes 120).</summary>
    public const int MaxSkills = 120;

    /// <summary>The most turned-down skill uses of one player (the backend takes 400).</summary>
    public const int MaxRejected = 400;

    /// <summary>The most boss health points (the backend takes 400 pairs).</summary>
    public const int MaxHpPoints = 400;

    private static int Tenths(double seconds) => (int)Math.Clamp(Math.Round(seconds * 10), 0, 100_000);

    /// <summary>One player's skill uses and deaths in the window; null when the stream named no skill use for them.</summary>
    public static TimelineUpload? For(FightExtras extras, int playerId, DateTime start, DateTime end, Func<int, string?> nameOf)
    {
        RotationReport report = RotationAnalysis.Build(extras, playerId, start, end, Array.Empty<DamageEvent>(), nameOf);
        var skills = new List<TimelineSkillUpload>();
        var indexOfName = new Dictionary<string, int>();
        var times = new List<int>();
        var index = new List<int>();
        foreach (RotationUse use in report.Uses)
        {
            // a passive skill is no use; one name has an id per level, the first seen stands for it
            if (use.Passive || times.Count >= MaxUses)
            {
                continue;
            }

            if (!indexOfName.TryGetValue(use.Name, out int at))
            {
                if (skills.Count >= MaxSkills)
                {
                    continue;
                }

                at = skills.Count;
                indexOfName[use.Name] = at;
                skills.Add(new TimelineSkillUpload(use.SkillId, use.Name));
            }

            times.Add(Tenths(use.At));
            index.Add(at);
        }

        if (times.Count == 0)
        {
            return null;
        }

        var deaths = (extras.Deaths ?? Array.Empty<DeathRecord>())
            .Where(d => d.Entity == playerId && d.At >= start && d.At <= end)
            .Select(d => Tenths((d.At - start).TotalSeconds))
            .Take(40)
            .ToList();
        // the server only answers the own character's skill uses: nothing for the others
        var rejected = (extras.Rejected ?? Array.Empty<RejectRecord>())
            .Where(r => r.Entity == playerId && r.At >= start && r.At <= end)
            .Select(r => Tenths((r.At - start).TotalSeconds))
            .Take(MaxRejected)
            .ToList();
        return new TimelineUpload(skills, times, index, deaths, rejected.Count > 0 ? rejected : null);
    }

    /// <summary>The boss's health curve in the window as flattened (tenths of a second, thousandths) pairs, at most one point per half second and <see cref="MaxHpPoints"/> points; null when there is none.</summary>
    public static IReadOnlyList<int>? BossHp(FightExtras extras, int bossId, DateTime start, DateTime end)
    {
        HpSeries? series = extras.Hp?.FirstOrDefault(h => h.Entity == bossId);
        if (series is null || series.T.Length == 0)
        {
            return null;
        }

        var flat = new List<int>();
        double lastAt = double.NegativeInfinity;
        for (int i = 0; i < series.T.Length; i++)
        {
            double at = (series.Start.AddMilliseconds(series.T[i]) - start).TotalSeconds;
            if (at < 0 || at > (end - start).TotalSeconds + 1)
            {
                continue;
            }

            if (at - lastAt < 0.5 && i != series.T.Length - 1)
            {
                continue;
            }

            lastAt = at;
            flat.Add(Tenths(at));
            flat.Add(Math.Clamp((int)series.Permille[i], 0, 1000));
            if (flat.Count >= MaxHpPoints * 2)
            {
                break;
            }
        }

        return flat.Count >= 4 ? flat : null;
    }
}
