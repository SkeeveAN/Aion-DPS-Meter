namespace AionDPS.Combat;

/// <summary>
/// Tells the two teams of the 10 against 10 arena apart. Nobody hits a team mate there, so every hit from one player on another says the two
/// stand on opposite sides; chained over the fight that splits all players into two sides, even those who never met. Which side is one's own is
/// told by the players known to be on it (the local player, the party roster).
/// </summary>
public sealed class ArenaTeams
{
    private readonly Dictionary<int, int> _parent = new();
    private readonly Dictionary<int, int> _parity = new(); // 0 = same side as the parent, 1 = the other side

    /// <summary>The ids that took part in a hit on another player.</summary>
    public IReadOnlyCollection<int> Ids => _parent.Keys;

    public void Clear()
    {
        _parent.Clear();
        _parity.Clear();
    }

    /// <summary>A hit of one player on another: they are on opposite sides.</summary>
    public void NoteHit(int source, int target)
    {
        if (source == target)
        {
            return;
        }

        (int rootA, int parityA) = Find(source);
        (int rootB, int parityB) = Find(target);
        if (rootA == rootB)
        {
            return; // already placed (a contradiction, say a charm effect, is ignored: the first answer stands)
        }

        _parent[rootA] = rootB;
        _parity[rootA] = parityA ^ parityB ^ 1;
    }

    /// <summary>Which side an id is on relative to another: true same, false opposite, null when no chain of hits connects them.</summary>
    public bool? SameSide(int a, int b)
    {
        if (!_parent.ContainsKey(a) || !_parent.ContainsKey(b))
        {
            return a == b ? true : null;
        }

        (int rootA, int parityA) = Find(a);
        (int rootB, int parityB) = Find(b);
        return rootA == rootB ? parityA == parityB : null;
    }

    /// <summary>Whether the id is on the side of the given own players: true own, false opponent, null unknown. The first own player connected to the id decides.</summary>
    public bool? IsOwnSide(int id, IEnumerable<int> ownPlayers)
    {
        foreach (int own in ownPlayers)
        {
            if (SameSide(id, own) is bool same)
            {
                return same;
            }
        }

        return null;
    }

    private (int Root, int Parity) Find(int id)
    {
        if (!_parent.ContainsKey(id))
        {
            _parent[id] = id;
            _parity[id] = 0;
            return (id, 0);
        }

        int parity = 0;
        int current = id;
        while (_parent[current] != current)
        {
            parity ^= _parity[current];
            current = _parent[current];
        }

        // compress the path: every node on it points at the root with its own parity
        int node = id;
        int nodeParity = parity;
        while (_parent[node] != node)
        {
            int next = _parent[node];
            int nextParity = nodeParity ^ _parity[node];
            _parent[node] = current;
            _parity[node] = nodeParity;
            node = next;
            nodeParity = nextParity;
        }

        return (current, parity);
    }
}
