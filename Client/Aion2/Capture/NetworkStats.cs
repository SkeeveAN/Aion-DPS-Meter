namespace AionDPS.Aion2.Capture;

/// <summary>What the network overlay shows, at one moment. Null numbers = nothing measured yet.</summary>
public sealed record NetworkSnapshot(
    bool Connected,
    string? Server,
    TimeSpan? Uptime,
    int Connections,
    double? PingMs,
    double? PingAverageMs,
    double? PingMinMs,
    double? PingMaxMs,
    double? JitterMs,
    double DownKBps,
    double UpKBps,
    double DownPeakKBps,
    double DownSegmentsPerSecond,
    int Retransmissions,
    int Gaps,
    TimeSpan? SinceLastData,
    int Queued,
    long Dropped);

/// <summary>
/// Measures the quality of the game connection from the traffic the capture already sees - nothing is sent.
/// Ping is estimated like a TCP stack does: the time from a segment the client sends to the server's
/// acknowledgement of it (Karn's rule: retransmitted segments give no sample). Because the capture sits on the
/// player's own PC, that is the whole path to the server. An acknowledgement that rides on game data waits for
/// the server's own processing, so the figure is an estimate, a little above the pure network time.
/// Fed from the capture's worker thread, read from the UI thread.
/// </summary>
public static class NetworkStats
{
    private const int WindowSeconds = 60;
    private const int PingWindowSeconds = 30;
    private const int MaxPendingSamples = 64;

    private static readonly object Gate = new();
    private static readonly Bucket[] Buckets = new Bucket[WindowSeconds];
    private static readonly List<(DateTime At, double Ms)> Pings = new();

    private static Connection? _connection;
    private static int _connections;
    private static DateTime _lastData = DateTime.MinValue;

    /// <summary>Where the capture reports how far behind its worker is and what it had to throw away.</summary>
    public static Func<(int Queued, long Dropped)>? QueueProbe { get; set; }

    private struct Bucket
    {
        public long Second;
        public long DownBytes;
        public long UpBytes;
        public int DownSegments;
        public int Retransmissions;
        public int Gaps;
    }

    private sealed class Connection
    {
        public string Server = "";
        public string Client = "";
        public DateTime Since;
        public bool HaveServerEnd;
        public uint ServerEnd;
        public bool HaveClientEnd;
        public uint ClientEnd;
        public readonly Queue<(uint End, DateTime At)> Sent = new();
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Array.Clear(Buckets);
            Pings.Clear();
            _connection = null;
            _connections = 0;
            _lastData = DateTime.MinValue;
        }
    }

    /// <summary>One captured segment, with or without payload (a bare acknowledgement is what ping needs).</summary>
    public static void Note(TcpSegment segment)
    {
        lock (Gate)
        {
            int length = segment.Payload.Length;
            Connection? c = _connection;
            bool matches = c is not null && (segment.FromServer
                ? segment.Source == c.Server && segment.Destination == c.Client
                : segment.Source == c.Client && segment.Destination == c.Server);
            if (!matches)
            {
                if (length == 0)
                {
                    return; // an acknowledgement of a connection that is not the game's
                }

                c = _connection = new Connection
                {
                    Server = segment.FromServer ? segment.Source : segment.Destination,
                    Client = segment.FromServer ? segment.Destination : segment.Source,
                    Since = segment.Timestamp,
                };
                _connections++;
                Pings.Clear();
            }

            ref Bucket bucket = ref BucketAt(segment.Timestamp);
            if (length > 0)
            {
                _lastData = segment.Timestamp;
            }

            uint end = unchecked(segment.Sequence + (uint)length);
            if (segment.FromServer)
            {
                if (length > 0)
                {
                    bucket.DownBytes += length;
                    bucket.DownSegments++;
                    if (c!.HaveServerEnd)
                    {
                        int ahead = (int)(segment.Sequence - c.ServerEnd);
                        if ((int)(end - c.ServerEnd) <= 0)
                        {
                            bucket.Retransmissions++;
                        }
                        else if (ahead > 0)
                        {
                            bucket.Gaps++;
                        }
                    }

                    if (!c.HaveServerEnd || (int)(end - c.ServerEnd) > 0)
                    {
                        c.ServerEnd = end;
                        c.HaveServerEnd = true;
                    }
                }

                // The server acknowledges what the client sent: the newest segment it covers gives the sample.
                if (segment.Ack != 0 || length > 0)
                {
                    (uint End, DateTime At)? covered = null;
                    while (c!.Sent.Count > 0 && (int)(segment.Ack - c.Sent.Peek().End) >= 0)
                    {
                        covered = c.Sent.Dequeue();
                    }

                    if (covered is { } hit)
                    {
                        double ms = (segment.Timestamp - hit.At).TotalMilliseconds;
                        if (ms is >= 0 and < 5000)
                        {
                            Pings.Add((segment.Timestamp, ms));
                            Trim(segment.Timestamp);
                        }
                    }
                }
            }
            else if (length > 0)
            {
                bucket.UpBytes += length;
                bool fresh = !c!.HaveClientEnd || (int)(end - c.ClientEnd) > 0;
                if (fresh)
                {
                    c.ClientEnd = end;
                    c.HaveClientEnd = true;
                    c.Sent.Enqueue((end, segment.Timestamp));
                    while (c.Sent.Count > MaxPendingSamples)
                    {
                        c.Sent.Dequeue();
                    }
                }
                else
                {
                    bucket.Retransmissions++;
                }
            }
        }
    }

    private static ref Bucket BucketAt(DateTime at)
    {
        long second = at.Ticks / TimeSpan.TicksPerSecond;
        ref Bucket bucket = ref Buckets[(int)(second % WindowSeconds)];
        if (bucket.Second != second)
        {
            bucket = new Bucket { Second = second };
        }

        return ref bucket;
    }

    private static void Trim(DateTime now)
    {
        DateTime from = now.AddSeconds(-PingWindowSeconds);
        int stale = 0;
        while (stale < Pings.Count && Pings[stale].At < from)
        {
            stale++;
        }

        if (stale > 0)
        {
            Pings.RemoveRange(0, stale);
        }
    }

    public static NetworkSnapshot Snapshot()
    {
        (int queued, long dropped) = QueueProbe?.Invoke() ?? (0, 0);
        lock (Gate)
        {
            DateTime now = DateTime.Now;
            long nowSecond = now.Ticks / TimeSpan.TicksPerSecond;
            Trim(now);

            double down = 0, up = 0, peak = 0, segments = 0;
            int retrans = 0, gaps = 0;
            foreach (Bucket b in Buckets)
            {
                if (b.Second <= nowSecond - WindowSeconds || b.Second > nowSecond)
                {
                    continue;
                }

                retrans += b.Retransmissions;
                gaps += b.Gaps;
                peak = Math.Max(peak, b.DownBytes / 1024.0);

                // the two seconds just completed give the current rate (the running second is partial)
                if (b.Second is var s && (s == nowSecond - 1 || s == nowSecond - 2))
                {
                    down += b.DownBytes / 1024.0 / 2;
                    up += b.UpBytes / 1024.0 / 2;
                    segments += b.DownSegments / 2.0;
                }
            }

            double? current = null, average = null, min = null, max = null, jitter = null;
            if (Pings.Count > 0)
            {
                var recent = Pings.Skip(Math.Max(0, Pings.Count - 5)).Select(p => p.Ms).OrderBy(m => m).ToList();
                current = recent[recent.Count / 2];
                average = Pings.Average(p => p.Ms);
                min = Pings.Min(p => p.Ms);
                max = Pings.Max(p => p.Ms);
                if (Pings.Count > 1)
                {
                    double sum = 0;
                    for (int i = 1; i < Pings.Count; i++)
                    {
                        sum += Math.Abs(Pings[i].Ms - Pings[i - 1].Ms);
                    }

                    jitter = sum / (Pings.Count - 1);
                }
            }

            Connection? c = _connection;
            TimeSpan? since = _lastData == DateTime.MinValue ? null : now - _lastData;
            return new NetworkSnapshot(c is not null, c?.Server, c is null ? null : now - c.Since, _connections,
                current, average, min, max, jitter, down, up, peak, segments, retrans, gaps, since, queued, dropped);
        }
    }
}
