using System.Net;
using System.Net.NetworkInformation;

namespace AionDPS.Aion2.Capture;

/// <summary>What the probe saw in the last minute: the answer time of the router and of the internet, and how many probes got none.</summary>
public sealed record NetworkProbeSnapshot(bool Running, string? Gateway, double? GatewayMs, int GatewayLost, int GatewayProbes, double? InternetMs, int InternetLost, int InternetProbes);

/// <summary>
/// Tells where a freeze comes from. Once a second it pings the router (the default gateway), a public host (1.1.1.1) and the game server itself; three tiny packets, nothing of the game.
/// A game stall while the router did not answer is the LAN or the router, one while only the internet did not answer is the line, and one while both answered
/// is not the local network (the game server or the route to it). Everything that goes wrong is written to <see cref="NetworkLog"/>.
/// </summary>
public static class NetworkProbe
{
    private const string InternetHost = "1.1.1.1";
    private const int KeepSeconds = 900;
    private const int WindowSeconds = 60;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private sealed record Sample(DateTime At, bool GatewayTried, double? GatewayMs, double? InternetMs, bool ServerTried, double? ServerMs);

    private static readonly object Gate = new();
    private static readonly List<Sample> Samples = new();
    private static CancellationTokenSource? _cts;
    private static string? _gateway;

    public static void Start()
    {
        lock (Gate)
        {
            if (_cts is not null)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
            NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
            CancellationToken token = _cts.Token;
            _ = Task.Run(() => Loop(token));
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            if (_cts is null)
            {
                return;
            }

            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
            _cts.Cancel();
            _cts = null;
            Samples.Clear();
        }
    }

    private static void OnAddressChanged(object? sender, EventArgs e) => NetworkLog.Write("network: an address of this PC changed (a new IP, a renewed lease or a reconnect of the router)");

    private static void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => NetworkLog.Write($"network: Windows reports the network as {(e.IsAvailable ? "available" : "NOT available")}");

    /// <summary>The IPv4 default gateway (whatever router it is) of the adapter the game connection runs over; before there is one, of the first active adapter. Null if none.</summary>
    private static string? FindGateway()
    {
        string? local = NetworkStats.LocalAddress;
        string? fallback = null;
        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties properties = nic.GetIPProperties();
                string? gateway = properties.GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Equals(IPAddress.Any))?.ToString();
                if (gateway is null)
                {
                    continue;
                }

                if (local is not null && properties.UnicastAddresses.Any(u => u.Address.ToString() == local))
                {
                    return gateway; // the adapter that carries the game
                }

                fallback ??= gateway;
            }
        }
        catch (NetworkInformationException)
        {
        }

        return fallback;
    }

    private static async Task<double?> PingAsync(string? host)
    {
        if (host is null)
        {
            return null;
        }

        try
        {
            using var ping = new Ping();
            PingReply reply = await ping.SendPingAsync(host, (int)Timeout.TotalMilliseconds).ConfigureAwait(false);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            return null;
        }
    }

    private static async Task Loop(CancellationToken token)
    {
        DateTime nextGatewayLookup = DateTime.MinValue;
        DateTime? gatewayDownSince = null, internetDownSince = null;
        while (!token.IsCancellationRequested)
        {
            DateTime started = DateTime.Now;
            if (started >= nextGatewayLookup)
            {
                string? found = FindGateway();
                if (found != _gateway)
                {
                    NetworkLog.Write($"network: router (default gateway) is {found ?? "unknown"}" + (_gateway is null ? "" : $", was {_gateway}"));
                }

                _gateway = found;
                nextGatewayLookup = started.AddSeconds(30);
            }

            string? gateway = _gateway;
            string? server = NetworkStats.ServerAddress;
            Task<double?> gw = PingAsync(gateway), net = PingAsync(InternetHost), srv = PingAsync(server);
            double? gwMs = await gw.ConfigureAwait(false), netMs = await net.ConfigureAwait(false), srvMs = await srv.ConfigureAwait(false);
            DateTime now = DateTime.Now;
            lock (Gate)
            {
                Samples.Add(new Sample(started, gateway is not null, gwMs, netMs, server is not null, srvMs));
                Samples.RemoveAll(s => s.At < now.AddSeconds(-KeepSeconds));
            }

            Track("router", gateway is not null, gwMs is not null, started, ref gatewayDownSince);
            Track("internet (" + InternetHost + ")", true, netMs is not null, started, ref internetDownSince);
            try
            {
                await Task.Delay(Math.Max(50, 1000 - (int)(DateTime.Now - started).TotalMilliseconds), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>A run of probes without an answer is written when it ends (or, for a long one, once it is two seconds old).</summary>
    private static void Track(string what, bool tried, bool answered, DateTime at, ref DateTime? downSince)
    {
        if (!tried)
        {
            return;
        }

        if (!answered)
        {
            downSince ??= at;
            return;
        }

        if (downSince is { } since)
        {
            double seconds = (at - since).TotalSeconds;
            if (seconds >= 2)
            {
                NetworkLog.Write(FormattableString.Invariant($"network: {what} did not answer for {seconds:0.#} s (from {since:HH:mm:ss})"), at);
            }

            downSince = null;
        }
    }

    /// <summary>Where a stall between two moments came from: "lan" (the router did not answer), "line" (the router did, the internet did not),
    /// "server" (both answered: not the local network), or null when the probe has too few samples there.</summary>
    public static string? Verdict(DateTime from, DateTime to)
    {
        lock (Gate)
        {
            var inside = Samples.Where(s => s.At >= from.AddSeconds(-1) && s.At <= to).ToList();
            if (inside.Count < 2)
            {
                return null;
            }

            int gatewayLost = inside.Count(s => s.GatewayTried && s.GatewayMs is null);
            int internetLost = inside.Count(s => s.InternetMs is null);
            return gatewayLost >= 2 ? "lan" : internetLost >= 2 ? "line" : "server";
        }
    }

    public static (int Gateway, int Internet, int Probes) LostBetween(DateTime from, DateTime to)
    {
        lock (Gate)
        {
            var inside = Samples.Where(s => s.At >= from.AddSeconds(-1) && s.At <= to).ToList();
            return (inside.Count(s => s.GatewayTried && s.GatewayMs is null), inside.Count(s => s.InternetMs is null), inside.Count);
        }
    }

    /// <summary>The game server's own ping between two moments: how many probes got no answer, how many were sent, and the average and worst answer time.
    /// Before is the number of answers in the 30 s before it (a server that never answers ping says nothing when it is silent now).</summary>
    public static (int Lost, int Probes, double? Avg, double? Max, int AnsweredBefore) ServerBetween(DateTime from, DateTime to)
    {
        lock (Gate)
        {
            var inside = Samples.Where(s => s.ServerTried && s.At >= from.AddSeconds(-1) && s.At <= to).ToList();
            var answers = inside.Where(s => s.ServerMs is not null).Select(s => s.ServerMs!.Value).ToList();
            int before = Samples.Count(s => s.ServerTried && s.ServerMs is not null && s.At < from.AddSeconds(-1) && s.At >= from.AddSeconds(-31));
            return (inside.Count - answers.Count, inside.Count, answers.Count > 0 ? answers.Average() : null, answers.Count > 0 ? answers.Max() : null, before);
        }
    }

    /// <summary>How slowly the router and the internet answered between two moments (average and worst of the answers that came), null where none came.</summary>
    public static (double? GatewayAvg, double? GatewayMax, double? InternetAvg, double? InternetMax) LatencyBetween(DateTime from, DateTime to)
    {
        lock (Gate)
        {
            var inside = Samples.Where(s => s.At >= from.AddSeconds(-1) && s.At <= to).ToList();
            var gateway = inside.Where(s => s.GatewayMs is not null).Select(s => s.GatewayMs!.Value).ToList();
            var internet = inside.Where(s => s.InternetMs is not null).Select(s => s.InternetMs!.Value).ToList();
            return (gateway.Count > 0 ? gateway.Average() : null, gateway.Count > 0 ? gateway.Max() : null,
                internet.Count > 0 ? internet.Average() : null, internet.Count > 0 ? internet.Max() : null);
        }
    }

    /// <summary>What kind of adapter carries the game connection (cable, Wi-Fi, a tunnel such as a VPN) and how fast it is, for the log; null if it cannot be found.</summary>
    public static string? AdapterInfo(string? local)
    {
        if (local is null)
        {
            return null;
        }

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && nic.GetIPProperties().UnicastAddresses.Any(u => u.Address.ToString() == local))
                {
                    return FormattableString.Invariant($"{nic.NetworkInterfaceType}, {nic.Speed / 1_000_000} Mbit/s, \"{nic.Name}\"");
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return null;
    }

    /// <summary>One probe second as the network report lists it: how fast the router, the internet host and the game server answered (null = no answer; the Tried flags tell a missing answer from a probe that was not sent).</summary>
    public readonly record struct ProbeSample(DateTime At, bool GatewayTried, double? GatewayMs, double? InternetMs, bool ServerTried, double? ServerMs);

    /// <summary>The probe seconds since a moment (the probe keeps the last fifteen minutes, and only while it runs).</summary>
    public static IReadOnlyList<ProbeSample> History(DateTime from)
    {
        lock (Gate)
        {
            return Samples.Where(s => s.At >= from).Select(s => new ProbeSample(s.At, s.GatewayTried, s.GatewayMs, s.InternetMs, s.ServerTried, s.ServerMs)).ToList();
        }
    }

    /// <summary>Whether the probe is running now.</summary>
    public static bool IsRunning
    {
        get
        {
            lock (Gate)
            {
                return _cts is not null;
            }
        }
    }

    public static NetworkProbeSnapshot Snapshot()
    {
        lock (Gate)
        {
            DateTime from = DateTime.Now.AddSeconds(-WindowSeconds);
            var recent = Samples.Where(s => s.At >= from).ToList();
            var tried = recent.Where(s => s.GatewayTried).ToList();
            double? Last(IEnumerable<double?> values) => values.LastOrDefault(v => v is not null);
            return new NetworkProbeSnapshot(_cts is not null, _gateway, Last(tried.Select(s => s.GatewayMs)), tried.Count(s => s.GatewayMs is null), tried.Count,
                Last(recent.Select(s => s.InternetMs)), recent.Count(s => s.InternetMs is null), recent.Count);
        }
    }
}
