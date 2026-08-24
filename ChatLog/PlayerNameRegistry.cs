namespace AionSniffer.ChatLog;

/// <summary>
/// Assigns stable synthetic object ids to combatant names seen in Chat.log, so the existing
/// DamageEvent/DpsCalculator/LiveAggregator machinery (built around int object ids for the
/// network path) can be reused as-is for the log-based path -- only the source of DamageEvents
/// changes, not the math consuming them.
///
/// IMPORTANT, documented limitation: Chat.log always writes the local character as the literal
/// name "You", never the real character name. If two clients on this machine share one Chat.log
/// (confirmed: this OriginAion install's file interleaves two clients' lines -- see README) and
/// BOTH are in combat at the same time, their damage is indistinguishable and gets attributed to
/// the same "You" identity here. Not a bug to "fix" without more information than the log
/// contains; the caller's own multi-boxing setup (one active fighter, one idle for group
/// requirements) happens to avoid this, but a future user with two simultaneously fighting
/// characters would see them merged.
/// </summary>
public sealed class PlayerNameRegistry
{
    private readonly Dictionary<string, int> _ids = new();
    private readonly Dictionary<int, string> _names = new();
    private int _next = 1;

    public int GetOrAssignId(string name)
    {
        if (_ids.TryGetValue(name, out int id))
        {
            return id;
        }

        id = _next++;
        _ids[name] = id;
        _names[id] = name;
        return id;
    }

    public string? NameFor(int id) => _names.GetValueOrDefault(id);
}
