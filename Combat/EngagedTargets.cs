namespace AionSniffer.Combat;

/// <summary>
/// Drops damage belonging to a fight the local player's side was not part of.
///
/// <para>Exists because two Aion clients can share one Chat.log, and the second one is not
/// necessarily anywhere near the first. In a real Sauro Supply Base run it sat next to a training
/// dummy in town, and the two strangers whacking that dummy -- 1.8M damage between them -- ranked
/// 6th and 7th in a run they were never part of.</para>
/// </summary>
public static class EngagedTargets
{
    /// <summary>
    /// Keeps an event when its target belongs to the fight: something our side attacked, or
    /// someone on our side being attacked.
    ///
    /// <para>That second half was missing at first, and it cost a whole player. In a 1v1 arena the
    /// opponent only ever attacks YOU -- every one of their lines has the local player as its
    /// target -- so filtering purely on "targets our side attacked" threw away all 78 of their
    /// damage lines and left them out of the grid entirely, in a duel where they are the only
    /// other person present.</para>
    ///
    /// <para>Returns everything unchanged when the local player never appears in a damage line at
    /// all: there is nothing to anchor on, and blanking the grid would be worse than showing too
    /// much.</para>
    /// </summary>
    public static List<DamageEvent> Filter(IReadOnlyList<DamageEvent> events, int youId)
    {
        var seedTargets = new HashSet<int>();
        foreach (DamageEvent e in events)
        {
            if (e.SourceObjectId == youId)
            {
                seedTargets.Add(e.TargetObjectId);
            }
            else if (e.TargetObjectId == youId)
            {
                // Either direction, so a pure healer who deals no damage but gets hit still seeds.
                seedTargets.Add(e.SourceObjectId);
            }
        }

        if (seedTargets.Count == 0)
        {
            return events.ToList();
        }

        // Our side: whoever attacked something we are fighting. This is what keeps a mob the tank
        // pulled and the local player never touched inside the session.
        var ownSide = new HashSet<int> { youId };
        foreach (DamageEvent e in events)
        {
            if (seedTargets.Contains(e.TargetObjectId))
            {
                ownSide.Add(e.SourceObjectId);
            }
        }

        var engaged = new HashSet<int>(seedTargets);
        engaged.UnionWith(ownSide);

        foreach (DamageEvent e in events)
        {
            if (ownSide.Contains(e.SourceObjectId))
            {
                engaged.Add(e.TargetObjectId);
            }
        }

        return events.Where(e => engaged.Contains(e.TargetObjectId)).ToList();
    }
}
