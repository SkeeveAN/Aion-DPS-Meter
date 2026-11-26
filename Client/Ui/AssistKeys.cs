namespace AionDPS.Ui;

/// <summary>
/// What the assist shows is told apart by a key: the skill or effect id, with one more bit for an effect that another player put on you. The same effect
/// from a second Gladiator in the group is another circle than your own.
/// </summary>
public static class AssistKeys
{
    private const int GroupBit = 1 << 30;

    public static int Make(int skillId, bool group) => group ? skillId | GroupBit : skillId;

    public static int SkillOf(int key) => key & ~GroupBit;

    public static bool IsGroup(int key) => (key & GroupBit) != 0;

    /// <summary>The keys in the player's order; the ones the order does not know come last, mine before the group's.</summary>
    public static List<int> Ordered(IEnumerable<int> keys, IReadOnlyList<int> order)
    {
        var position = new Dictionary<int, int>();
        for (int i = 0; i < order.Count; i++)
        {
            position.TryAdd(order[i], i);
        }

        return keys.Distinct()
            .OrderBy(k => position.TryGetValue(k, out int p) ? p : int.MaxValue)
            .ThenBy(k => IsGroup(k) ? 1 : 0)
            .ToList();
    }

    /// <summary>The order after <paramref name="moved"/> was dropped before or after <paramref name="target"/>; <paramref name="shown"/> is what the list showed.</summary>
    public static List<int> Move(IReadOnlyList<int> shown, IReadOnlyList<int> order, int moved, int target, bool after)
    {
        var list = shown.Where(k => k != moved).ToList();
        int at = list.IndexOf(target);
        list.Insert(at < 0 ? list.Count : at + (after ? 1 : 0), moved);
        return list.Concat(order.Where(k => !list.Contains(k))).ToList();
    }

    private static HashSet<int>? _known;

    /// <summary>Notes what the assist has seen running: new keys go into the settings file (once), where the lists of the assist window find them.</summary>
    public static void Remember(IReadOnlyList<int> keys)
    {
        if (keys.Count == 0)
        {
            return;
        }

        _known ??= MeterSettings.Load().AssistKnown.ToHashSet();
        var added = keys.Where(k => !_known.Contains(k)).ToList();
        if (added.Count == 0)
        {
            return;
        }

        MeterSettings settings = MeterSettings.Load();
        foreach (int key in added)
        {
            _known.Add(key);
            if (!settings.AssistKnown.Contains(key))
            {
                settings.AssistKnown.Add(key);
            }
        }

        settings.Save();
    }
}
