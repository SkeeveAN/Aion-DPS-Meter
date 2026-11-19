using System.Diagnostics;
using AionDPS.Aion2;

namespace AionDPS.Ui;

public enum PvpAlertKind
{
    EnemyNear,
    TargetedByEnemy,
}

public sealed record PvpAlert(PvpAlertKind Kind, string Name);

/// <summary>
/// The PvP alerts in the Abyss (the idea, the frames and the rules are Stacy's fork's, 2026-11-14, MIT; ported here with our own enemy test): in the Abyss maps (zones 20, 22 and 23: the lower, middle and upper
/// Chaotic Reshanta) it tells when an enemy comes into view (one sound for the first enemy who comes while nobody was there; it is armed again once no enemy has been in view for <see cref="EmptyFor"/>) and when an
/// enemy marks the local player as target (the same player not more often than every <see cref="Cooldown"/>). Out of the Abyss nothing sounds. An enemy is a player of the other faction (see
/// <see cref="Aion2EntityDirectory.IsHostilePlayer"/>); party members never count. Every alert is written to the trace with the faction byte and the server the meter knows, so it can be checked afterwards.
/// Pure: the directory and the time are handed in, so a test can walk through it.
/// </summary>
public sealed class PvpWatch
{
    /// <summary>The Abyss maps: lower, middle and upper Chaotic Reshanta.</summary>
    public static bool IsAbyss(int zoneId) => zoneId is 20 or 22 or 23;

    /// <summary>The same player does not set off the same alert more often than this.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);

    /// <summary>No enemy in view for this long before the "enemy near" alert may sound again.</summary>
    public static readonly TimeSpan EmptyFor = TimeSpan.FromSeconds(1);

    private DateTime? _emptySince;
    private bool _nearArmed;
    private readonly Dictionary<int, DateTime> _lastTargeted = new();
    private long _targetSeq;
    private bool _active;

    /// <summary>Whether the watch is on (the player is in the Abyss).</summary>
    public bool Active => _active;

    private static string Describe(Aion2EntityDirectory entities, int id) =>
        $"faction byte {entities.FactionByteOf(id)} (own {entities.OwnFactionByte}), server {(entities.ServerIdOf(id) is int server ? server.ToString() : "unknown")}";

    /// <summary>One look. Returns the alerts to play now (none while out of the Abyss). <paramref name="near"/> and <paramref name="targeted"/> are the two options.</summary>
    public IReadOnlyList<PvpAlert> Poll(Aion2EntityDirectory entities, int zoneId, DateTime now, bool near, bool targeted)
    {
        if (!IsAbyss(zoneId))
        {
            _active = false;
            return [];
        }

        entities.TargetsSince(_targetSeq, out long total);
        if (!_active)
        {
            // Just came in: what is already in view is no news.
            _active = true;
            _lastTargeted.Clear();
            _emptySince = null;
            _nearArmed = false;
            _targetSeq = total;
            return [];
        }

        var alerts = new List<PvpAlert>();
        int me = entities.LocalPlayerId;
        IReadOnlySet<string> party = entities.PartyNames;

        // The enemies in view now (the first-sight frame lists them, the "gone" frame takes them away); the state is kept up even while the option is off.
        (int Id, string Name)? first = null;
        foreach (int id in entities.HostilePlayersInView())
        {
            if (id != me && entities.NameFor(id) is { } name && !name.StartsWith("Player #", StringComparison.Ordinal) && !party.Contains(name))
            {
                first = (id, name);
                break;
            }
        }

        if (first is null)
        {
            _emptySince ??= now;
            _nearArmed |= now - _emptySince.Value >= EmptyFor;
        }
        else
        {
            _emptySince = null;
            if (_nearArmed)
            {
                _nearArmed = false;
                if (near)
                {
                    alerts.Add(new PvpAlert(PvpAlertKind.EnemyNear, first.Value.Name));
                    Trace.WriteLine($"[pvp alert] enemy near: {first.Value.Name}, {Describe(entities, first.Value.Id)}, zone {zoneId}");
                }
            }
        }

        IReadOnlyList<(int Actor, int Target, DateTime At)> since = entities.TargetsSince(_targetSeq, out total);
        _targetSeq = total;
        if (targeted && me >= 0)
        {
            foreach ((int actor, int target, _) in since)
            {
                if (target != me || actor == me)
                {
                    continue;
                }

                string? name = entities.NameFor(actor);
                if (name is null || name.StartsWith("Player #", StringComparison.Ordinal) || party.Contains(name) || !entities.IsHostilePlayer(actor))
                {
                    continue; // a monster, a party member or a friend: not a threat
                }

                if (_lastTargeted.TryGetValue(actor, out DateTime last) && now - last < Cooldown)
                {
                    continue;
                }

                _lastTargeted[actor] = now;
                alerts.Add(new PvpAlert(PvpAlertKind.TargetedByEnemy, name));
                Trace.WriteLine($"[pvp alert] marked by an enemy: {name}, {Describe(entities, actor)}, zone {zoneId}");
            }
        }

        return alerts;
    }
}
