using AionDPS.Combat.Sources;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AionDPS.Aion2.Capture;

/// <summary>
/// Reads the game's TCP traffic off every network adapter through Npcap and hands each segment's
/// payload to a callback. Filtered in the driver (BPF) to the game-server ports, so nothing else
/// on the machine is ever copied into this process; when no port is known yet (calibration
/// recordings) it falls back to all TCP. Purely passive: no packet is ever sent, and the game
/// process is never touched. Callbacks run on SharpPcap's capture threads.
/// </summary>
public sealed class NpcapCaptureService : IDisposable
{
    private readonly IReadOnlyList<int> _serverPorts;
    private readonly Action<TcpSegment> _onSegment;
    private readonly Action<SourceState, string> _onStatus;
    private readonly List<ILiveDevice> _devices = new();
    private long _packets;

    public NpcapCaptureService(IReadOnlyList<int> serverPorts, Action<TcpSegment> onSegment, Action<SourceState, string> onStatus)
    {
        _serverPorts = serverPorts;
        _onSegment = onSegment;
        _onStatus = onStatus;
    }

    /// <summary>"ip:port" of the game server, learned from the first server-side segment.</summary>
    public string? ServerEndpoint { get; private set; }

    public long Packets => Interlocked.Read(ref _packets);

    public string Filter => _serverPorts.Count == 0
        ? "tcp"
        : "tcp and (" + string.Join(" or ", _serverPorts.Select(p => $"port {p}")) + ")";

    public void Start()
    {
        try
        {
            var all = CaptureDeviceList.Instance;
            if (all.Count == 0)
            {
                _onStatus(SourceState.Error, "No Npcap capture devices found. Is Npcap installed and the service running?");
                return;
            }

            int opened = 0;
            foreach (ILiveDevice device in all)
            {
                // Loopback and disconnected adapters are harmless to include; a failing one is
                // skipped rather than aborting the whole capture (VPN adapters do this).
                try
                {
                    device.Open(new DeviceConfiguration { ReadTimeout = 250, Mode = DeviceModes.None });
                    device.Filter = Filter;
                    device.OnPacketArrival += OnPacketArrival;
                    device.StartCapture();
                    _devices.Add(device);
                    opened++;
                }
                catch (PcapException)
                {
                    device.Dispose();
                }
            }

            if (opened == 0)
            {
                _onStatus(SourceState.Error, $"Npcap found {all.Count} device(s) but none could be opened.");
                return;
            }

            _onStatus(SourceState.Waiting, $"Capturing on {opened} adapter(s), filter \"{Filter}\" - waiting for game traffic.");
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or PcapException)
        {
            _onStatus(SourceState.Error, $"Packet capture could not start: {ex.GetBaseException().Message}");
        }
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        RawCapture raw = e.GetPacket();
        Packet packet;
        try
        {
            packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        }
        catch (Exception)
        {
            return;
        }

        TcpPacket? tcp = packet.Extract<TcpPacket>();
        IPPacket? ip = packet.Extract<IPPacket>();
        if (tcp is null || ip is null || tcp.PayloadData is not { Length: > 0 } payload)
        {
            return;
        }

        bool fromServer = _serverPorts.Count == 0 ? tcp.SourcePort < tcp.DestinationPort : _serverPorts.Contains(tcp.SourcePort);
        string source = $"{ip.SourceAddress}:{tcp.SourcePort}";
        string destination = $"{ip.DestinationAddress}:{tcp.DestinationPort}";
        if (fromServer && ServerEndpoint is null)
        {
            ServerEndpoint = source;
            _onStatus(SourceState.Connected, $"Game traffic from {source}.");
        }

        Interlocked.Increment(ref _packets);
        _onSegment(new TcpSegment(raw.Timeval.Date.ToLocalTime(), source, destination, tcp.SequenceNumber, payload, fromServer));
    }

    public void Dispose()
    {
        foreach (ILiveDevice device in _devices)
        {
            try
            {
                device.StopCapture();
                device.Close();
            }
            catch (PcapException)
            {
                // Already gone (adapter removed mid-session) - nothing left to release.
            }
        }

        _devices.Clear();
    }
}
