namespace AionDPS.Aion2.Capture;

/// <summary>
/// Finds the game server's port without being told: the server sends a small keepalive frame (length byte 0x0E, opcode
/// 0x0036) all the time, and a segment that starts with it is the mark of the game stream (7,045 of 22,752 server segments
/// of one recording of 2026-10-14). Three such segments from one port within ten seconds name that port. Only the first
/// three bytes of a payload are looked at and nothing is kept.
/// </summary>
public sealed class GameFlowDetector
{
    public const int Needed = 3;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Dictionary<int, (int Count, DateTime First)> _seen = new();

    /// <summary>True the moment the third keepalive of a port within the window arrives.</summary>
    public bool Observe(int sourcePort, ReadOnlySpan<byte> payload, DateTime now)
    {
        if (payload.Length < 3 || payload[0] != 0x0E || payload[1] != 0x00 || payload[2] != 0x36)
        {
            return false;
        }

        if (!_seen.TryGetValue(sourcePort, out var entry) || now - entry.First > Window)
        {
            if (_seen.Count > 256)
            {
                _seen.Clear();
            }

            entry = (0, now);
        }

        entry.Count++;
        _seen[sourcePort] = entry;
        return entry.Count >= Needed;
    }

    public void Reset() => _seen.Clear();
}
