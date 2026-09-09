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
        var oneWayHits = new HashSet<(int From, int To)>();
        var fought = new Dictionary<int, HashSet<int>>();
        var sameTarget = new Dictionary<int, HashSet<int>>();

        foreach (DamageEvent e in events)
        {
            bool sourceIsPlayer = isPlayer(e.SourceObjectId);
            bool targetIsPlayer = isPlayer(e.TargetObjectId);

            if (e.IsHeal)
            {
                // The "You" exclusion this class exists to get right -- see the remarks above.
                if (sourceIsPlayer && targetIsPlayer && e.SourceObjectId != e.TargetObjectId
                    && e.SourceObjectId != youId && e.TargetObjectId != youId)
                {
                    Link(allies, e.SourceObjectId, e.TargetObjectId);
                }
            }
            else if (sourceIsPlayer && targetIsPlayer && e.SourceObjectId != e.TargetObjectId)
            {
                oneWayHits.Add((e.SourceObjectId, e.TargetObjectId));
            }
            else if (sourceIsPlayer && !targetIsPlayer)
            {
                // Everyone hitting this mob, so people who only ever deal damage can still be
                // placed. Without it a group with no healer -- or one whose healer heals the local
                // player, whose heals are unusable here -- stayed entirely unclassified.
                if (!sameTarget.TryGetValue(e.TargetObjectId, out var attackers))
                {
                    sameTarget[e.TargetObjectId] = attackers = new HashSet<int>();
                }

                attackers.Add(e.SourceObjectId);
            }
        }

        // A hit sourced by the LOCAL PLAYER specifically needs the reverse hit too before it counts
        // as hostility - found from a real report: "Zetsu received 950 bleeding damage after you
        // used Blade Rampage", a real Dredgion teammate caught by the local player's own AOE/DoT,
        // who obviously never hit back. That one one-way "You hit Zetsu" edge was enough to seed
        // Zetsu as an enemy outright; since "enemies are fixed" once seeded (see the remarks
        // below), growing the enemy side through the ally graph from that single bad edge dragged
        // in every other real teammate connected to Zetsu by a heal, flipping the whole group
        // except the local player. Every OTHER hit (an enemy hitting you, or two third parties
        // fighting each other) is trusted on one line alone, same as before - the local player is
        // the one participant whose own outgoing AOE this meter can actually mis-attribute as
        // hostility; nothing says a THIRD party's hit on someone else is similarly suspect.
        foreach ((int from, int to) in oneWayHits)
        {
            if (from != youId || oneWayHits.Contains((to, from)))
            {
                Link(fought, from, to);
            }
        }

        var known = new HashSet<int> { youId };
        foreach (DamageEvent e in events)
        {
            foreach (int id in new[] { e.SourceObjectId, e.TargetObjectId })
            {
                if (isPlayer(id) && nameOf(id) is string n && anchors.Contains(n))
                {
                    known.Add(id);
                }
            }
        }

        // Hostility is settled from the PROVABLE core only -- the anchors plus whoever heals with
        // them -- and settled before allies are grown any further. Both halves of that are
        // load-bearing, and both were got wrong first:
        //
        // Seeding from anchors alone missed an enemy whose only fight was against a non-anchor
        // teammate. Seeding from the fully grown side instead cascaded catastrophically: the
        // shared-target rule had already pulled the opposing team in, so their opponents -- our own
        // group -- came back out as enemies. Against two real Dredgion runs that mislabelled three
        // of five teammates in one and one of five in the other.
        var core = Grow(known, allies);
        var enemies = Grow(Seed(core, fought), allies, core);
        enemies.ExceptWith(core);

        // Only now the looser signal: sharing a mob with someone already on our side. Enemies are
        // fixed by this point, so a Dredgion's shared instance mobs cannot drag an opponent in.
        var own = new HashSet<int>(core);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (HashSet<int> attackers in sameTarget.Values)
            {
                if (attackers.Overlaps(own))
                {
                    changed |= Absorb(own, enemies, attackers);
                }
            }

            foreach (int id in own.ToList())
            {
                changed |= Absorb(own, enemies, allies.GetValueOrDefault(id));
            }
        }

        var side = new Dictionary<int, Side>();
        foreach (int id in own)
        {
            side[id] = Side.Own;
        }

        foreach (int id in enemies)
        {
            side[id] = Side.Enemy;
        }

        return side;
    }

    /// <summary>Everyone who traded blows with someone already known to be on our side.</summary>
    private static HashSet<int> Seed(HashSet<int> known, Dictionary<int, HashSet<int>> fought)
    {
        var seed = new HashSet<int>();
        foreach (int id in known)
        {
            if (fought.TryGetValue(id, out var opponents))
            {
                seed.UnionWith(opponents);
            }
        }

        seed.ExceptWith(known);
        return seed;
    }

    /// <summary>Follows an edge set outward from a seed -- used to pull a side's healer in with it.</summary>
    private static HashSet<int> Grow(HashSet<int> seed, Dictionary<int, HashSet<int>> edges, IReadOnlySet<int>? block = null)
    {
        var result = new HashSet<int>(seed);
        var stack = new Stack<int>(seed);
        while (stack.Count > 0)
        {
            foreach (int other in edges.GetValueOrDefault(stack.Pop()) ?? Enumerable.Empty<int>())
            {
                if (block?.Contains(other) != true && result.Add(other))
                {
                    stack.Push(other);
                }
            }
        }

        return result;
    }

    private static bool Absorb(HashSet<int> own, HashSet<int> enemies, IEnumerable<int>? candidates)
    {
        bool changed = false;
        foreach (int id in candidates ?? Enumerable.Empty<int>())
        {
            if (!enemies.Contains(id) && own.Add(id))
            {
                changed = true;
            }
        }

        return changed;
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
