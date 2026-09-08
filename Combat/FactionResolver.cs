namespace AionSniffer.Combat;

/// <summary>Which side of a fight a player is on, relative to the local player.</summary>
public enum Side
{
    /// <summary>Nothing has connected this player to either side yet.</summary>
    Unknown,
    Own,
    Enemy,
}

/// <summary>
/// Works out who is fighting alongside the local player and who is fighting against them, from
/// nothing but the events Chat.log already produces. Aion never states a faction for anyone, so
/// this is derived rather than read, and the derivation rests on one hard rule of the game: you
/// cannot heal the opposing faction. A heal is therefore proof of alliance, and damage between
/// players is proof of the opposite.
///
/// <para>
/// Validated against a real Terath Dredgion (two runs, 2026-09-08): it separated both 6-a-side
/// teams with no misclassification in either direction -- 13 players in the first run, 11 in the
/// second.
/// </para>
///
/// <para><b>Why heals from "You" are ignored.</b> They are the one signal that looks strongest and
/// is actually poison. With two Aion clients writing into one Chat.log, each narrates its own
/// character as "You", so "You" is not one person. In the validation Dredgion the two clients were
/// on OPPOSITE sides, and "You" healed members of both teams -- following those edges merged the
/// two teams into a single 24-player blob where everyone was everyone's ally. Third-person heals
/// ("X recovered N HP because Y used Z") name both parties outright and cannot blur that way, so
/// only those build the graph. Trying instead to let damage evidence override a heal path was
/// measurably worse: it flipped the entire friendly group to "enemy" in both runs.</para>
/// </summary>
public static class FactionResolver
{
    /// <summary>
    /// <paramref name="anchors"/> are names known to be on the local player's side without needing
    /// the graph: the characters registered in Settings, and anyone Chat.log narrated a loot drop
    /// for, since loot lines only ever mention your own group. Without an anchor the two sides are
    /// still separated, but there is no way to tell which of them is yours.
    /// </summary>
    public static IReadOnlyDictionary<int, Side> Resolve(
        IReadOnlyList<DamageEvent> events,
        Func<int, string?> nameOf,
        Func<int, bool> isPlayer,
        int youId,
        IReadOnlySet<string> anchors)
    {
        var allies = new Dictionary<int, HashSet<int>>();
        var fought = new HashSet<(int, int)>();

        foreach (DamageEvent e in events)
        {
            if (e.SourceObjectId == e.TargetObjectId || !isPlayer(e.SourceObjectId) || !isPlayer(e.TargetObjectId))
            {
                continue;
            }

            if (e.IsHeal)
            {
                // The "You" exclusion this whole class exists to get right -- see the remarks above.
                if (e.SourceObjectId == youId || e.TargetObjectId == youId)
                {
                    continue;
                }

                Link(allies, e.SourceObjectId, e.TargetObjectId);
            }
            else
            {
                fought.Add((e.SourceObjectId, e.TargetObjectId));
                fought.Add((e.TargetObjectId, e.SourceObjectId));
            }
        }

        // Own side, seeded from the anchors and the local player, then grown through the heal
        // graph. Seeding FIRST and expanding second is load-bearing: an earlier version only
        // looked at players that had a heal edge, so an anchor who never healed and was never
        // healed -- which is most classes, and was the local player's own second-client row in a
        // real Sauro run -- was left Unknown despite being provably in the group.
        var side = new Dictionary<int, Side>();
        var own = new HashSet<int> { youId };
        foreach ((int id, HashSet<int> _) in allies)
        {
            if (nameOf(id) is string n && anchors.Contains(n))
            {
                own.Add(id);
            }
        }

        foreach (DamageEvent e in events)
        {
            foreach (int id in new[] { e.SourceObjectId, e.TargetObjectId })
            {
                if (isPlayer(id) && nameOf(id) is string n && anchors.Contains(n))
                {
                    own.Add(id);
                }
            }
        }

        // Everyone heal-connected to a known member is a member too.
        var queue = new Queue<int>(own);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!allies.TryGetValue(current, out var neighbours))
            {
                continue;
            }

            foreach (int other in neighbours)
            {
                if (own.Add(other))
                {
                    queue.Enqueue(other);
                }
            }
        }

        foreach (int id in own)
        {
            side[id] = Side.Own;
        }

        // Anyone who traded damage with our side is on the other one. Applied after the components
        // above so a player with no heals at all -- common, most classes never heal -- still gets
        // classified from their fighting alone.
        foreach ((int a, int b) in fought)
        {
            if (side.GetValueOrDefault(a) == Side.Own && side.GetValueOrDefault(b) != Side.Own)
            {
                side[b] = Side.Enemy;
            }
        }

        return side;
    }

    private static void Link(Dictionary<int, HashSet<int>> graph, int a, int b)
    {
        if (!graph.TryGetValue(a, out var setA))
        {
            graph[a] = setA = new HashSet<int>();
        }

        if (!graph.TryGetValue(b, out var setB))
        {
            graph[b] = setB = new HashSet<int>();
        }

        setA.Add(b);
        setB.Add(a);
    }
}
