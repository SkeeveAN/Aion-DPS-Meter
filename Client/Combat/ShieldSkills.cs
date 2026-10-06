namespace AionDPS.Combat;

/// <summary>
/// Shields, which Aion 2 reports as events flagged as heals. Counted as healing they credited players with
/// "healing" they never did and hid who shielded whom, so they are kept apart from healing (see
/// <see cref="LiveAggregator.Shields"/>).
/// <list type="bullet">
/// <item>Personal shields every class has (Defiance, Warding Shield): the event is the player onto themselves.</item>
/// <item>The Chanter's and Templar's group shield (Protection Circle): one event per recipient with the
/// recipient as the SOURCE and the caster as the TARGET (checked on runs 2026-10-06/07: Aahz -> Miko and
/// Pencilgon -> Miko, both 2,955, with Miko the Chanter), the shield's strength as the amount.</item>
/// </list>
/// </summary>
public static class ShieldSkills
{
    private static readonly HashSet<string> Personal = new(StringComparer.Ordinal) { "Defiance", "Warding Shield" };
    private static readonly HashSet<string> Group = new(StringComparer.Ordinal) { "Protection Circle" };

    public static bool IsShield(in DamageEvent e) => e.IsHeal && e.Skill is { } skill && (Personal.Contains(skill) || Group.Contains(skill));

    /// <summary>A shield cast on others: the event's target is the caster, its source the one shielded.</summary>
    public static bool IsGroupShield(in DamageEvent e) => e.IsHeal && e.Skill is { } skill && Group.Contains(skill);
}
