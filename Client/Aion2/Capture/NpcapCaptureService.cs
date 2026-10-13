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
    private readonly string? _adapterId;
    private readonly List<ILiveDevice> _devices = new();
    private long _packets;

    // The capture threads only parse and enqueue; decoding, the ring buffer and the debug file run on
    // one worker. Before this, all of that ran inside the Npcap callback, so a burst (a world boss)
    // overflowed the driver's buffer and packets were lost.
    private const int MaxQueued = 400_000;
    private const int KernelBufferBytes = 32 * 1024 * 1024;
    private readonly System.Collections.Concurrent.BlockingCollection<TcpSegment> _queue = new(MaxQueued);
    private Thread? _worker;
    private long _dropped;

    public NpcapCaptureService(IReadOnlyList<int> serverPorts, Action<TcpSegment> onSegment, Action<SourceState, string> onStatus, string? adapterId = null)
    {
        _adapterId = adapterId;
        _serverPorts = serverPorts;
        _onSegment = onSegment;
        _onStatus = onStatus;
    }

    /// <summary>"ip:port" of the game server, learned from the first server-side segment.</summary>
    public string? ServerEndpoint { get; private set; }

    public long Packets => Interlocked.Read(ref _packets);

    /// <summary>Packets whose handling threw. A bad packet is counted and skipped; before this, one
    /// exception in the callback ended the capture thread without a word.</summary>
    public int CallbackErrors => Volatile.Read(ref _callbackErrors);

    public string? LastCallbackError { get; private set; }

    /// <summary>Segments thrown away because the worker fell more than <see cref="MaxQueued"/> behind.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    private int _callbackErrors;

    /// <summary>Optional BPF network (for example <c>193.202.112.0/24</c>): everything TCP to or from it,
    /// whatever the port. Used by the diagnostic recording to find traffic - chat, say - that runs on
    /// another connection than the combat stream.</summary>
    public string? Network { get; init; }

    public string Filter => Network is { Length: > 0 }
        ? $"tcp and net {Network}"
        : _serverPorts.Count == 0
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

            IReadOnlyList<ILiveDevice> selected = CaptureAdapters.Select(all.Cast<ILiveDevice>().ToList(), _adapterId, out string? selectionNote);
            int opened = 0;
            NetworkStats.Reset();
            NetworkStats.QueueProbe = () => (_queue.Count, Dropped);
            _worker ??= StartWorker();
            foreach (ILiveDevice device in selected)
            {
                // Loopback and disconnected adapters are harmless to include; a failing one is
                // skipped rather than aborting the whole capture (VPN adapters do this).
                try
                {
                    device.Open(new DeviceConfiguration { ReadTimeout = 250, Mode = DeviceModes.None, KernelBufferSize = KernelBufferBytes });
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

            _onStatus(SourceState.Waiting, $"Capturing on {opened} adapter(s), filter \"{Filter}\" - waiting for game traffic." + (selectionNote is null ? "" : $" ({selectionNote})"));
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
        if (tcp is null || ip is null)
        {
            return;
        }

        // A bare acknowledgement has no payload; it only feeds the ping estimate (NetworkStats).
        byte[] payload = tcp.PayloadData ?? Array.Empty<byte>();
        if (payload.Length == 0 && !tcp.Acknowledgment)
        {
            return;
        }

        bool fromServer = _serverPorts.Count == 0 ? tcp.SourcePort < tcp.DestinationPort : _serverPorts.Contains(tcp.SourcePort);
        string source = $"{ip.SourceAddress}:{tcp.SourcePort}";
        string destination = $"{ip.DestinationAddress}:{tcp.DestinationPort}";
        if (fromServer && payload.Length > 0 && ServerEndpoint is null)
        {
            ServerEndpoint = source;
            _onStatus(SourceState.Connected, $"Game traffic from {source}.");
        }

        if (payload.Length > 0)
        {
            Interlocked.Increment(ref _packets);
        }

        try
        {
            if (!_queue.TryAdd(new TcpSegment(raw.Timeval.Date.ToLocalTime(), source, destination, tcp.SequenceNumber, payload, fromServer, tcp.AcknowledgmentNumber, tcp.WindowSize, ip.TimeToLive)))
            {
                Interlocked.Increment(ref _dropped);
            }
        }
        catch (InvalidOperationException)
        {
            // Queue already completed: the capture is being stopped.
        }
    }

    private Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (TcpSegment segment in _queue.GetConsumingEnumerable())
            {
                try
                {
                    NetworkStats.Note(segment);
                    if (segment.Payload.Length > 0)
                    {
                        _onSegment(segment);
                    }
                }
                catch (Exception ex)
                {
                    // Never let a handler's exception end the worker.
                    Interlocked.Increment(ref _callbackErrors);
                    LastCallbackError = $"{ex.GetType().Name}: {ex.Message}";
                }
            }
        })
        {
            IsBackground = true,
            Name = "Aion2 segment worker",
        };
        thread.Start();
        return thread;
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
        NetworkStats.QueueProbe = null;
        _queue.CompleteAdding();
    }
}
