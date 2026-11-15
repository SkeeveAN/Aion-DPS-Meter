using System.Collections.ObjectModel;

namespace AionDPS.Ui;

/// <summary>One boss fight of the last instance with the players' rows as they were counted (kept after the meter is cleared for the next instance).</summary>
public sealed class BossSnapshot
{
    public int TargetId { get; init; }

    public string Name { get; init; } = "";

    public DateTime Start { get; init; }

    public DateTime End { get; init; }

    public double Seconds { get; init; }

    public bool Killed { get; init; }

    public bool Wiped { get; init; }

    /// <summary>The last attempt is still being fought.</summary>
    public bool Running { get; init; }

    public long Total { get; init; }

    public IReadOnlyList<PlayerRow> Rows { get; init; } = Array.Empty<PlayerRow>();

    /// <summary>The number of the fight on aiondps.com, once the upload answered with it.</summary>
    public long? EncounterId { get; set; }

    public string Link => EncounterId is long id ? "https://aiondps.com/encounters/" + id : "";
}

/// <summary>
/// The bosses of the last instance, newest on top. Filled while the meter counts and kept when it is cleared (leaving the instance, the Clear button), until
/// the next instance has a boss fight of its own: the right list of the live meter page shows them.
/// </summary>
public sealed class LastInstanceStore
{
    private string _signature = "";
    private readonly Dictionary<(int Target, DateTime Start), long> _ids = new();

    public string Title { get; private set; } = "";

    public IReadOnlyList<BossSnapshot> Bosses { get; private set; } = Array.Empty<BossSnapshot>();

    /// <summary>Something that is shown changed (a new boss, new numbers, a link).</summary>
    public event Action? Changed;

    public void Replace(string title, List<BossSnapshot> bosses)
    {
        foreach (BossSnapshot boss in bosses)
        {
            if (_ids.TryGetValue((boss.TargetId, boss.Start), out long id))
            {
                boss.EncounterId = id;
            }
        }

        string keys = string.Join(";", bosses.Select(b => b.TargetId + "@" + b.Start.Ticks));
        string signature = keys + "|" + string.Join(";", bosses.Select(b => b.Total + "/" + b.Killed + "/" + b.Running + "/" + b.EncounterId + "/" + b.Rows.Count));
        if (signature == _signature)
        {
            return;
        }

        // the title is the place of the first sight of these bosses: it stays while the same bosses are updated (the zone may already have changed when the meter is cleared)
        if (Title.Length == 0 || !_signature.StartsWith(keys + "|", StringComparison.Ordinal))
        {
            Title = title;
        }

        _signature = signature;
        Bosses = bosses;
        Changed?.Invoke();
    }

    /// <summary>The upload of a boss fight answered with its number: the fight that started nearest to <paramref name="startedAt"/> (within a minute) gets the link.</summary>
    public void SetEncounterId(DateTime startedAt, long id)
    {
        BossSnapshot? match = Bosses.OrderBy(b => Math.Abs((b.Start - startedAt).TotalSeconds)).FirstOrDefault();
        if (match is null || Math.Abs((match.Start - startedAt).TotalSeconds) > 60)
        {
            return;
        }

        _ids[(match.TargetId, match.Start)] = id;
        match.EncounterId = id;
        _signature += "+" + id;
        Changed?.Invoke();
    }
}
