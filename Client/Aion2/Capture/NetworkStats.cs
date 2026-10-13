namespace AionDPS.Aion2.Capture;

/// <summary>What the network overlay shows, at one moment. Null numbers = nothing measured yet.</summary>
public sealed record NetworkSnapshot(
    bool Connected,
    string? Server,
    TimeSpan? Uptime,
    int Switches,
    int Reconnects,
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
    long Dropped,
    int StallCount,
    double LongestStallSeconds,
    TimeSpan? SinceLastStall,
    string? LastStallVerdict,
    TimeSpan? UnackedFor,
    NetworkProbeSnapshot Probe);

/// <summary>A pause of the game connection: the server sent nothing for a while although it had been busy, and what the PC saw meanwhile.</summary>
public sealed record NetworkStall(DateTime Start, DateTime End, int ClientSegments, int ServerAcks, int Retransmissions, string? Verdict)
{
    public double Seconds => (End - Start).TotalSeconds;
}

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
    private const int StallSeconds = 3;       // the server was silent for this long ...
    private const int BusySegments = 50;      // ... on a connection that had carried at least this many segments
    private const int StallWindowMinutes = 10;
    private const int HighPingMs = 300;
    private const int LossLogThreshold = 5;   // repeated or missing segments within 10 seconds that are worth a log line

    private static readonly object Gate = new();
    private static readonly Bucket[] Buckets = new Bucket[WindowSeconds];
    private static readonly List<(DateTime At, double Ms)> Pings = new();
    private static readonly List<NetworkStall> Stalls = new();

    private static Connection? _connection;
    private static int _switches;
    private static int _reconnects;
    private static DateTime _sessionSince;
    private static DateTime _lastData = DateTime.MinValue;
    private static long _checkedSecond;
    private static DateTime _lastLossLog = DateTime.MinValue;
    private static DateTime _lastPingLog = DateTime.MinValue;

    /// <summary>The IP address of this PC on the game connection (without the port), or null before there is one.</summary>
    public static string? LocalAddress
    {
        get
        {
            lock (Gate)
            {
                string? client = _connection?.Client;
                int colon = client?.LastIndexOf(':') ?? -1;
                return client is null ? null : colon > 0 ? client[..colon] : client;
            }
        }
    }

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
        public DateTime LastServerData = DateTime.MinValue;
        public int DataSegments;
        public int GapClientSegments;
        public int GapServerAcks;
        public int GapRetransmissions;
    }

    public static void Reset()
    {
        lock (Gate)
        {
            Array.Clear(Buckets);
            Pings.Clear();
            Stalls.Clear();
            _checkedSecond = 0;
            _lastLossLog = DateTime.MinValue;
            _lastPingLog = DateTime.MinValue;
            _connection = null;
            _switches = 0;
            _reconnects = 0;
            _sessionSince = DateTime.MinValue;
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

                string server = segment.FromServer ? segment.Source : segment.Destination;
                string how;
                if (_connection is null || segment.Timestamp - _lastData > TimeSpan.FromSeconds(30))
                {
                    _sessionSince = segment.Timestamp; // the first connection, or one after a long silence: a new session
                    how = "new session";
                }
                else if (_connection.Server == server)
                {
                    _reconnects++; // the same server again: the connection was lost and made anew
                    how = "RECONNECT to the same server (the connection was lost)";
                }
                else
                {
                    _switches++; // another server (a zone, an instance): normal
                    how = "server change (zone or instance)";
                }

                NetworkLog.Write($"connection: {how}, game server {server}", segment.Timestamp);

                c = _connection = new Connection
                {
                    Server = server,
                    Client = segment.FromServer ? segment.Destination : segment.Source,
                    Since = segment.Timestamp,
                };
                Pings.Clear();
            }

            ref Bucket bucket = ref BucketAt(segment.Timestamp);
            CheckLoss(segment.Timestamp);
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
                    else
                    {
                        c.GapRetransmissions++;
                    }

                    if (c.LastServerData != DateTime.MinValue && c.DataSegments >= BusySegments && segment.Timestamp - c.LastServerData >= TimeSpan.FromSeconds(StallSeconds))
                    {
                        RecordStall(c, segment.Timestamp);
                    }

                    c.LastServerData = segment.Timestamp;
                    c.DataSegments++;
                    c.GapClientSegments = 0;
                    c.GapServerAcks = 0;
                    c.GapRetransmissions = 0;
                }
                else
                {
                    c!.GapServerAcks++;
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
                            if (ms >= HighPingMs && segment.Timestamp - _lastPingLog >= TimeSpan.FromSeconds(30))
                            {
                                _lastPingLog = segment.Timestamp;
                                NetworkLog.Write($"HIGH PING {ms:0} ms to {c.Server} (a round trip of the game connection)", segment.Timestamp);
                            }

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
                    c.GapClientSegments++;
                    c.Sent.Enqueue((end, segment.Timestamp));
                    while (c.Sent.Count > MaxPendingSamples)
                    {
                        c.Sent.Dequeue();
                    }
                }
                else
                {
                    bucket.Retransmissions++;
                    c.GapRetransmissions++;
                }
            }
        }
    }

    /// <summary>Once a second: if the last 10 seconds had many repeated or missing segments, write one line (at most one every 30 s).</summary>
    private static void CheckLoss(DateTime at)
    {
        long second = at.Ticks / TimeSpan.TicksPerSecond;
        if (second == _checkedSecond)
        {
            return;
        }

        _checkedSecond = second;
        if (at - _lastLossLog < TimeSpan.FromSeconds(30))
        {
            return;
        }

        int retransmissions = 0, gaps = 0;
        foreach (Bucket b in Buckets)
        {
            if (b.Second >= second - 10 && b.Second < second)
            {
                retransmissions += b.Retransmissions;
                gaps += b.Gaps;
            }
        }

        if (retransmissions + gaps >= LossLogThreshold)
        {
            _lastLossLog = at;
            double? ping = Pings.Count > 0 ? Pings.Average(p => p.Ms) : null;
            NetworkLog.Write($"PACKET LOSS in the last 10 s: {retransmissions} repeated, {gaps} missing segments" + (ping is { } p ? $"; ping on average {p:0} ms" : ""), at);
        }
    }

    /// <summary>The server's data came back after a pause: keep it, ask the probe where it came from, and write it to the log.</summary>
    private static void RecordStall(Connection c, DateTime end)
    {
        DateTime start = c.LastServerData;
        string? verdict = NetworkProbe.Verdict(start, end);
        var stall = new NetworkStall(start, end, c.GapClientSegments, c.GapServerAcks, c.GapRetransmissions, verdict);
        Stalls.Add(stall);
        if (Stalls.Count > 50)
        {
            Stalls.RemoveAt(0);
        }

        var (gatewayLost, internetLost, probes) = NetworkProbe.LostBetween(start, end);
        string probe = probes < 2 ? "probe: no data" : $"probe: router lost {gatewayLost}/{probes}, internet lost {internetLost}/{probes}";
        string meaning = verdict switch
        {
            "lan" => "the router did not answer: LAN cable, PC network card or the router itself",
            "line" => "the router answered, the internet did not: the line or the provider",
            "server" => "router and internet answered: not the local network (game server or the route to it)",
            _ => "cause unknown (probe off or too few samples)",
        };
        string seconds = stall.Seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        NetworkLog.Write($"STALL {seconds} s ({start:HH:mm:ss.f} - {end:HH:mm:ss.f}) to {c.Server}: the client sent {c.GapClientSegments} segments, " +
            $"{c.GapServerAcks} acknowledgements came from the server, {c.GapRetransmissions} repeated segments; {probe} -> {meaning}", end);
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

            int stallCount = 0;
            double longest = 0;
            NetworkStall? last = null;
            foreach (NetworkStall st in Stalls)
            {
                if (st.End >= now.AddMinutes(-StallWindowMinutes))
                {
                    stallCount++;
                    longest = Math.Max(longest, st.Seconds);
                    last = st;
                }
            }

            Connection? c = _connection;
            TimeSpan? unacked = c is not null && c.Sent.Count > 0 ? now - c.Sent.Peek().At : null;
            TimeSpan? since = _lastData == DateTime.MinValue ? null : now - _lastData;
            return new NetworkSnapshot(c is not null, c?.Server, c is null ? null : now - _sessionSince, _switches, _reconnects,
                current, average, min, max, jitter, down, up, peak, segments, retrans, gaps, since, queued, dropped,
                stallCount, longest, last is null ? null : now - last.End, last?.Verdict, unacked, NetworkProbe.Snapshot());
        }
    }
}
