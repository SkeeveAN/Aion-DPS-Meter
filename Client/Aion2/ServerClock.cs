namespace AionDPS.Aion2;

/// <summary>
/// The game server's clock, read from the frame the server sends about every 50 ms (opcode 0x0036: two opcode bytes and the UTC time in milliseconds,
/// little endian). It is UTC and carries no time zone; what it gives is how far this PC's clock is off (0.3 to 0.8 s in the recordings, the PC runs
/// slow), which the timetable's countdowns take into account. A value more than a minute off the PC clock is not taken (a replay of a recording, or a
/// PC whose clock is wrong by minutes: the timetable then simply goes by the PC).
/// </summary>
public static class ServerClock
{
    private static readonly object Gate = new();
    private static double _offsetMs;
    private static bool _known;

    /// <summary>How far the server is ahead of the PC (negative: behind). Zero until a server clock frame has been read.</summary>
    public static TimeSpan Offset
    {
        get
        {
            lock (Gate)
            {
                return TimeSpan.FromMilliseconds(_offsetMs);
            }
        }
    }

    /// <summary>The PC's local time, corrected by the server's offset.</summary>
    public static DateTime Now => DateTime.Now + Offset;

    /// <summary>Takes one reading of the server's clock (UTC milliseconds) against the PC's clock at <paramref name="pcUtc"/>.</summary>
    public static void Note(long serverUtcMs, DateTime pcUtc)
    {
        double difference = serverUtcMs - new DateTimeOffset(pcUtc, TimeSpan.Zero).ToUnixTimeMilliseconds();
        if (Math.Abs(difference) > 60_000)
        {
            return;
        }

        lock (Gate)
        {
            // a slow average: one reading is off by the network's delay, a few hundred keep that out
            _offsetMs = _known ? _offsetMs + (difference - _offsetMs) / 100 : difference;
            _known = true;
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _offsetMs = 0;
            _known = false;
        }
    }
}
