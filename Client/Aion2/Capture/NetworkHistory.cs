namespace AionDPS.Aion2.Capture;

/// <summary>One second of the game connection as the overlay shows it, for the network report.</summary>
public readonly record struct NetworkSecond(DateTime At, double? PingMs, double? JitterMs, double DownKBps, double UpKBps, int Retransmissions, int Gaps);

/// <summary>
/// The last fifteen minutes of the connection, one reading a second, so a report can say what the minutes before the click looked like. It only reads what
/// <see cref="NetworkStats"/> already measures (nothing is sent), starts with the meter and keeps nothing on disk.
/// </summary>
public static class NetworkHistory
{
    public static readonly TimeSpan Keep = TimeSpan.FromMinutes(15);

    private static readonly object Gate = new();
    private static readonly List<NetworkSecond> Seconds = new();
    private static Timer? _timer;

    public static void Start()
    {
        lock (Gate)
        {
            _timer ??= new Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
            Seconds.Clear();
        }
    }

    private static void Sample()
    {
        try
        {
            NetworkSnapshot s = NetworkStats.Snapshot();
            if (!s.Connected)
            {
                return;
            }

            DateTime now = DateTime.Now;
            lock (Gate)
            {
                Seconds.Add(new NetworkSecond(now, s.PingMs, s.JitterMs, s.DownKBps, s.UpKBps, s.Retransmissions, s.Gaps));
                int stale = Seconds.FindIndex(x => x.At >= now - Keep);
                if (stale > 0)
                {
                    Seconds.RemoveRange(0, stale);
                }
            }
        }
        catch (Exception)
        {
            // a reading that fails is a second missing in the report, nothing more
        }
    }

    /// <summary>The seconds since a moment, oldest first.</summary>
    public static IReadOnlyList<NetworkSecond> Since(DateTime from)
    {
        lock (Gate)
        {
            return Seconds.Where(x => x.At >= from).ToList();
        }
    }
}
