namespace AionDPS.Combat;

/// <summary>
/// Group and self shields. Aion 2 reports a shield to every player who gets it as an event from that
/// player onto themselves, flagged as a heal, with the shield's strength as the amount - the caster
/// is not named in it. Counted as healing it credited each recipient with "healing" they never did, and
/// hid who shielded whom; so they are kept apart from healing (see <see cref="LiveAggregator.Shields"/>)
/// and the caster is worked out from the class that owns the skill.
/// </summary>
public static class ShieldSkills
{
    // Names of the shield skills as the skill table spells them (Gladiator: Defiance; Templar: Warding Shield).
    private static readonly HashSet<string> Names = new(StringComparer.Ordinal) { "Defiance", "Warding Shield" };

    public static bool IsShield(in DamageEvent e) => e.IsHeal && e.Skill is { } skill && Names.Contains(skill);
}
