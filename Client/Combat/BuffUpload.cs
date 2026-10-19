using AionDPS.Aion2.Protocol;
using AionDPS.Upload;

namespace AionDPS.Combat;

/// <summary>
/// The effects that ran on a player during a fight, for the upload (the website's buff columns): each helpful effect with the share of the fight it was on
/// (<see cref="BuffUsageUpload.UptimePermille"/>), how often it was put on and who gave it. Pure, so a test can run it.
/// </summary>
public static class BuffUpload
{
    /// <summary>Under this share of the fight an effect is noise (a proc that flickered once).</summary>
    public const double MinUptimePercent = 3;

    /// <summary>The most effects one player carries in the upload (the backend takes 80).</summary>
    public const int MaxPerPlayer = 40;

    /// <summary>The potions of the game (item ids 20xxxxx): instant effects that say nothing about a player's preparation.</summary>
    private static bool IsPotion(int skillId) => skillId is >= 2_000_000 and < 2_100_000;

    public static IReadOnlyList<BuffUsageUpload> For(FightExtras extras, int playerId, DateTime start, DateTime end, Func<int, string?> nameOf)
    {
        RotationReport report = RotationAnalysis.Build(extras, playerId, start, end, Array.Empty<DamageEvent>(), nameOf);
        var list = new List<BuffUsageUpload>();
        foreach (BuffRow row in report.Buffs)
        {
            if (row.UptimePercent < MinUptimePercent || row.SkillId < 1000 || IsPotion(row.SkillId)
                || !Aion2Artwork.IsBeneficialEffect(row.SkillId) || Aion2Artwork.IsPassiveEffect(row.SkillId))
            {
                continue;
            }

            // "★" is the player themself; the upload names only the others (no giver at all = the player's own effect)
            var givers = row.Givers.Split(", ", StringSplitOptions.RemoveEmptyEntries).Where(g => g != "★").ToList();
            list.Add(new BuffUsageUpload(Aion2SkillNames.NameOf(row.SkillId), Math.Max(1, row.Applications), row.SkillId,
                (int)Math.Round(Math.Clamp(row.UptimePercent, 0, 100) * 10), givers, SpansOf(row.Spans)));
            if (list.Count >= MaxPerPlayer)
            {
                break;
            }
        }

        return list;
    }

    /// <summary>The most spans one effect carries in the upload (the backend takes 200 pairs).</summary>
    public const int MaxSpans = 200;

    /// <summary>Spans closer than this are one (a flicker of the same effect).</summary>
    private const double JoinGapSeconds = 0.5;

    /// <summary>The spans of an effect for the upload: start and end of each one flattened, in tenths of a second. Gaps under half a second are closed; when more than
    /// <see cref="MaxSpans"/> remain, the shortest gaps are closed until they fit.</summary>
    public static IReadOnlyList<int>? SpansOf(IReadOnlyList<(double Start, double End)>? spans)
    {
        if (spans is null || spans.Count == 0)
        {
            return null;
        }

        var list = new List<(double Start, double End)>();
        foreach (var (s, e) in spans.OrderBy(x => x.Start))
        {
            if (list.Count > 0 && s - list[^1].End < JoinGapSeconds)
            {
                list[^1] = (list[^1].Start, Math.Max(list[^1].End, e));
            }
            else
            {
                list.Add((s, e));
            }
        }

        while (list.Count > MaxSpans)
        {
            int at = 0;
            double smallest = double.MaxValue;
            for (int i = 1; i < list.Count; i++)
            {
                double gap = list[i].Start - list[i - 1].End;
                if (gap < smallest)
                {
                    smallest = gap;
                    at = i;
                }
            }

            list[at - 1] = (list[at - 1].Start, list[at].End);
            list.RemoveAt(at);
        }

        var flat = new List<int>(list.Count * 2);
        foreach (var (s, e) in list)
        {
            flat.Add(Tenths(s));
            flat.Add(Math.Max(Tenths(e), Tenths(s)));
        }

        return flat;
    }

    private static int Tenths(double seconds) => (int)Math.Clamp(Math.Round(seconds * 10), 0, 100_000);

    /// <summary>
    /// Drops what the group did not give: an effect with a named giver who is nobody of this fight came from a monster (it hit the whole party), so it is no buff.
    /// The givers that remain are the players of the fight only.
    /// </summary>
    public static IReadOnlyList<BuffUsageUpload> KeepGroupSources(IReadOnlyList<BuffUsageUpload> buffs, ISet<string> participants)
    {
        var kept = new List<BuffUsageUpload>(buffs.Count);
        foreach (BuffUsageUpload b in buffs)
        {
            if (b.Givers is not { Count: > 0 } givers)
            {
                kept.Add(b);
                continue;
            }

            var known = givers.Where(participants.Contains).ToList();
            if (known.Count > 0)
            {
                kept.Add(b with { Givers = known });
            }
        }

        return kept;
    }
}
