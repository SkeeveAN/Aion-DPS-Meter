namespace AionDPS.Combat;

/// <summary>
/// Shields, which Aion 2 reports as events flagged as heals. Counted as healing they credited players with
/// "healing" they never did, so they are kept apart from healing (see <see cref="LiveAggregator.Shields"/>).
/// <list type="bullet">
/// <item>Personal shields every class has (Defiance, Warding Shield): the event is the player onto themselves.</item>
/// <item>The group shields (the Chanter's and Templar's Protection Circle, the Chanter's Impeding Authority -
/// a Stigma: 16 % of the recipient's max HP for 20 s on the caster and the group within 40 m): the stream
/// reports no cast and no caster, only one event per absorbed hit - the attacker as the SOURCE, the shielded
/// player as the TARGET, the absorbed amount as the amount. Checked on the recordings of 2026-10-07 and
/// 2026-10-09: 1,605 of 1,607 Protection Circle events and 59 of 68 Impeding Authority events equal a hit
/// with the same time, source, target and amount (a hit absorbed whole is reported with an amount of 1).
/// They show as <see cref="IsAbsorb">absorbed damage</see> on the shielded player, never as a shield given.</item>
/// </list>
/// </summary>
public static class ShieldSkills
{
    // English names; the German ones are Schockaufhebung / Behütungsschild (personal) and Schutzformation /
    // Hoheit der Blockade (a Chanter's group shields). Matched through the skill id, never the shown
    // name, which follows the meter's language.
    private static readonly HashSet<string> Personal = new(StringComparer.Ordinal) { "Defiance", "Warding Shield" };
    private static readonly HashSet<string> Absorb = new(StringComparer.Ordinal) { "Protection Circle", "Impeding Authority" };

    private static string? EnglishName(in DamageEvent e)
    {
        if (e.SkillId != 0)
        {
            var table = Aion2.Protocol.Aion2SkillNames.Load();
            if (table.TryGetValue(e.SkillId, out string? name) || table.TryGetValue(e.SkillId / 10000 * 10000, out name))
            {
                return name;
            }
        }

        return e.Skill;
    }

    public static bool IsShield(in DamageEvent e) => e.IsHeal && EnglishName(e) is { } name && (Personal.Contains(name) || Absorb.Contains(name));

    /// <summary>A hit a group shield absorbed: the event's source is the attacker, its target the shielded player.
    /// Events naming one player as both ends (amount 1: the shield's own application or end) are no hit.</summary>
    public static bool IsAbsorb(in DamageEvent e) => e.IsHeal && e.SourceObjectId != e.TargetObjectId && EnglishName(e) is { } name && Absorb.Contains(name);
}
