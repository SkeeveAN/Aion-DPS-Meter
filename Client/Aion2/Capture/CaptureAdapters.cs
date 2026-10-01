using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SharpPcap;

namespace AionDPS.Aion2.Capture;

/// <summary>One network adapter the game's traffic could leave through.</summary>
public sealed record CaptureAdapter(string Id, string Name, string Description, string Ipv4, bool HasGateway, bool IsVirtual, bool IsVpn, bool IsRecommended)
{
    public string Label => $"{Name} - {Ipv4}" + (IsVpn ? " [VPN]" : IsVirtual ? " [virtual]" : "") + (IsRecommended ? " *" : "");
}

/// <summary>
/// Lists the machine's usable adapters and guesses which one carries the game. The guess is the
/// adapter Windows itself would route an internet address through; a gaming VPN (WTFast, ExitLag
/// ...) only wins that when it is the default route, which is why the choice is user-overridable.
/// Persisted as <see cref="MeterSettings.CaptureAdapterId"/>: empty = automatic, <see cref="AllAdapters"/>
/// = every adapter, anything else = <see cref="NetworkInterface.Id"/>.
/// </summary>
public static class CaptureAdapters
{
    public const string AllAdapters = "all";

    private static readonly string[] VirtualMarkers = { "hyper-v", "vethernet", "vmware", "virtualbox", "bluetooth", "wsl", "loopback", "npcap" };
    private static readonly string[] VpnMarkers = { "wtfast", "exitlag", "mudfish", "haste", "tap-windows", "wintun", "wireguard", "openvpn", "nordlynx", "vpn", "tunnel" };

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

    public static IReadOnlyList<CaptureAdapter> List()
    {
        int bestIndex = BestInterfaceIndex();
        var result = new List<(CaptureAdapter Adapter, int Score)>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback)
            {
                continue;
            }

            IPInterfaceProperties props = nic.GetIPProperties();
            string? ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            if (ipv4 is null)
            {
                continue;
            }

            string haystack = (nic.Name + " " + nic.Description).ToLowerInvariant();
            bool isVirtual = VirtualMarkers.Any(haystack.Contains);
            bool isVpn = nic.NetworkInterfaceType is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp || VpnMarkers.Any(haystack.Contains);
            bool hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            int index = 0;
            try
            {
                index = props.GetIPv4Properties().Index;
            }
            catch (NetworkInformationException)
            {
            }

            int score = (index == bestIndex ? 1000 : 0) + (hasGateway ? 100 : 0) + (isVirtual && !isVpn ? -500 : 0);
            result.Add((new CaptureAdapter(nic.Id, nic.Name, nic.Description, ipv4, hasGateway, isVirtual && !isVpn, isVpn, false), score));
        }

        string? bestId = result.OrderByDescending(r => r.Score).Select(r => r.Adapter.Id).FirstOrDefault();
        return result
            .OrderByDescending(r => r.Adapter.Id == bestId)
            .ThenByDescending(r => r.Score)
            .Select(r => r.Adapter with { IsRecommended = r.Adapter.Id == bestId })
            .ToList();
    }

    public static CaptureAdapter? Recommended(IReadOnlyList<CaptureAdapter> adapters) => adapters.FirstOrDefault(a => a.IsRecommended);

    /// <summary>
    /// The Npcap devices to capture on for the stored choice. Falls back to every device (with a
    /// note) when the choice cannot be matched, so a stale setting never silently captures nothing.
    /// </summary>
    public static IReadOnlyList<ILiveDevice> Select(IReadOnlyList<ILiveDevice> devices, string? adapterId, out string? note)
    {
        note = null;
        string? id = adapterId;
        if (string.IsNullOrEmpty(id))
        {
            id = Recommended(List())?.Id;
            if (id is null)
            {
                return devices;
            }
        }

        if (id == AllAdapters)
        {
            return devices;
        }

        var matched = devices.Where(d => d.Name.Contains(id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0)
        {
            note = $"adapter {id} not found among Npcap devices - capturing on all";
            return devices;
        }

        return matched;
    }

    private static int BestInterfaceIndex()
    {
        try
        {
            // 8.8.8.8 as a stand-in for "the internet"; only the route lookup matters, nothing is sent.
            uint dest = BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes(), 0);
            return GetBestInterface(dest, out uint index) == 0 ? (int)index : -1;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return -1;
        }
    }
}
