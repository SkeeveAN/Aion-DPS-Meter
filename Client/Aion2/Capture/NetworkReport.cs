using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using AionDPS.Update;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// The network report: what the last fifteen minutes of the game connection looked like, as one JSON document that a person and a program can read. It is saved to a file the
/// player chooses, or sent to the developer after the player has seen what is in it (see <see cref="Contents"/>).
/// What it never holds: an IP address of the player (the address of this PC, of the router or of the internet connection), the name of the PC or of the Windows account,
/// the name of an adapter, a MAC address, the name of a character, a legion or anything the game sent. The address of the game server is in it - it is the game's, not the player's.
/// </summary>
public static class NetworkReport
{
    private const int BucketSeconds = 5;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>The kinds of data a report holds, in the player's language (shown before sending). Keys of the language file, in order.</summary>
    public static readonly string[] Contents = { "Network.Report.Item.Quality", "Network.Report.Item.Probe", "Network.Report.Item.Stalls", "Network.Report.Item.Adapter", "Network.Report.Item.Server", "Network.Report.Item.System", "Network.Report.Item.Id" };

    /// <summary>The keys of what the report does not hold.</summary>
    public static readonly string[] NotContained = { "Network.Report.Not.Ip", "Network.Report.Not.Names", "Network.Report.Not.Game" };

    /// <summary>The file name the player is offered: version and the start of the period the report covers.</summary>
    public static string FileName(DateTime windowStart) => $"aiondps_{AppVersion.Text}_{windowStart:yyyy-MM-dd_HH-mm-ss}.json";

    /// <summary>When the period of a report made now starts: up to fifteen minutes back, but not before there is something to tell.</summary>
    public static DateTime WindowStart(DateTime now)
    {
        DateTime limit = now - NetworkHistory.Keep;
        DateTime first = now;
        IReadOnlyList<NetworkSecond> seconds = NetworkHistory.Since(limit);
        if (seconds.Count > 0)
        {
            first = seconds[0].At;
        }

        IReadOnlyList<NetworkProbe.ProbeSample> probes = NetworkProbe.History(limit);
        if (probes.Count > 0 && probes[0].At < first)
        {
            first = probes[0].At;
        }

        return first < limit ? limit : first;
    }

    private static double? Round(double? value) => value is { } v && !double.IsNaN(v) ? Math.Round(v, 1) : null;

    private static double? Average(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count > 0 ? Round(list.Average()) : null;
    }

    private static double? Max(IEnumerable<double?> values)
    {
        var list = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return list.Count > 0 ? Round(list.Max()) : null;
    }

    /// <summary>Builds the report of the last fifteen minutes. <paramref name="anonymousId"/> is only given when it is sent (the file for the player's own use does not need it).</summary>
    public static string Build(DateTime now, string? captureSelection, string? anonymousId)
    {
        DateTime start = WindowStart(now);
        IReadOnlyList<NetworkSecond> seconds = NetworkHistory.Since(start);
        IReadOnlyList<NetworkProbe.ProbeSample> probes = NetworkProbe.History(start);
        NetworkSnapshot snapshot = NetworkStats.Snapshot();

        var buckets = new List<object>();
        for (DateTime t = start; t < now; t = t.AddSeconds(BucketSeconds))
        {
            DateTime end = t.AddSeconds(BucketSeconds);
            var s = seconds.Where(x => x.At >= t && x.At < end).ToList();
            var p = probes.Where(x => x.At >= t && x.At < end).ToList();
            if (s.Count == 0 && p.Count == 0)
            {
                continue;
            }

            buckets.Add(new
            {
                at = (int)(t - start).TotalSeconds,
                pingMs = Average(s.Select(x => x.PingMs)),
                pingMaxMs = Max(s.Select(x => x.PingMs)),
                jitterMs = Average(s.Select(x => x.JitterMs)),
                downKBps = s.Count > 0 ? Round(s.Average(x => x.DownKBps)) : null,
                upKBps = s.Count > 0 ? Round(s.Average(x => x.UpKBps)) : null,
                routerMs = Average(p.Select(x => x.GatewayMs)),
                routerLost = p.Count(x => x.GatewayTried && x.GatewayMs is null),
                internetMs = Average(p.Select(x => x.InternetMs)),
                internetLost = p.Count(x => x.InternetMs is null),
                serverPingMs = Average(p.Select(x => x.ServerMs)),
                serverPingLost = p.Count(x => x.ServerTried && x.ServerMs is null),
            });
        }

        var stalls = NetworkStats.StallHistory(start).Select(st => new
        {
            at = (int)Math.Max(0, (st.Start - start).TotalSeconds),
            seconds = Round(st.Seconds),
            cause = st.Verdict,
            clientSegments = st.ClientSegments,
            serverAcks = st.ServerAcks,
            retransmissions = st.Retransmissions,
        }).ToList();

        var report = new
        {
            format = "aiondps-network-report",
            formatVersion = 1,
            clientVersion = AppVersion.Text,
            system = RuntimeInformation.OSDescription,
            npcapInstalled = NpcapAvailability.Detect().IsInstalled,
            anonymousId,
            createdAt = now.ToUniversalTime().ToString("O"),
            periodStart = start.ToUniversalTime().ToString("O"),
            periodSeconds = (int)(now - start).TotalSeconds,
            adapter = AdapterInfo(NetworkStats.LocalAddress, captureSelection),
            gameConnection = new
            {
                connected = snapshot.Connected,
                server = snapshot.Server,
                uptimeSeconds = snapshot.Uptime is { } up ? (int)up.TotalSeconds : (int?)null,
                switches = snapshot.Switches,
                reconnects = snapshot.Reconnects,
            },
            summary = new
            {
                pingAverageMs = Average(seconds.Select(x => x.PingMs)),
                pingMaxMs = Max(seconds.Select(x => x.PingMs)),
                jitterAverageMs = Average(seconds.Select(x => x.JitterMs)),
                downPeakKBps = Round(snapshot.DownPeakKBps),
                retransmissionsLastMinute = snapshot.Retransmissions,
                gapsLastMinute = snapshot.Gaps,
                stallCount = stalls.Count,
                longestStallSeconds = stalls.Count > 0 ? Round(stalls.Max(x => x.seconds ?? 0)) : null,
            },
            probe = new
            {
                running = NetworkProbe.IsRunning,
                routerAverageMs = Average(probes.Select(x => x.GatewayMs)),
                routerLost = probes.Count(x => x.GatewayTried && x.GatewayMs is null),
                internetAverageMs = Average(probes.Select(x => x.InternetMs)),
                internetLost = probes.Count(x => x.InternetMs is null),
                serverPingAverageMs = Average(probes.Select(x => x.ServerMs)),
                serverPingLost = probes.Count(x => x.ServerTried && x.ServerMs is null),
                seconds = probes.Count,
            },
            stalls,
            timeline = new { bucketSeconds = BucketSeconds, rows = buckets },
        };
        return JsonSerializer.Serialize(report, Json);
    }

    /// <summary>The kind and speed of the adapter that carries the game, whether it is a VPN or virtual, how many such adapters the PC has otherwise, and how the meter picked it - no names, no addresses.</summary>
    private static object AdapterInfo(string? local, string? selection)
    {
        string kind = "unknown";
        long? speed = null;
        try
        {
            if (local is not null)
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up && nic.GetIPProperties().UnicastAddresses.Any(u => u.Address.ToString() == local))
                    {
                        kind = nic.NetworkInterfaceType.ToString();
                        speed = nic.Speed / 1_000_000;
                        break;
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        IReadOnlyList<CaptureAdapter> all = CaptureAdapters.List();
        return new
        {
            type = kind,
            speedMbit = speed,
            vpnAdaptersOnPc = all.Count(a => a.IsVpn),
            virtualAdaptersOnPc = all.Count(a => a.IsVirtual),
            selection = string.IsNullOrEmpty(selection) ? "automatic" : selection == CaptureAdapters.AllAdapters ? "all" : "chosen",
        };
    }
}
