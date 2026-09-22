using AionDPS.ChatLog;
using AionDPS.Combat;
using AionDPS.Combat.Sources;

namespace AionDPS.Aion2;

/// <summary>
/// Aion 2 input: the UE5 client writes no Chat.log, so combat is read from the game's own network
/// traffic instead (Npcap capture of the TCP stream to the game server, then the game's frame and
/// opcode layout - the same approach AionFlex takes). Nothing is injected into or read from the
/// game process. This is the seam-side shell: it owns capture lifecycle and status reporting and
/// turns decoded frames into <see cref="DamageEvent"/>s once <see cref="Protocol.Aion2Protocol"/>
/// knows the opcodes. Until a real capture from an Aion 2 session has been used to calibrate that
/// layout, it reports exactly that and delivers nothing - never a guess.
/// </summary>
public sealed class Aion2PacketCombatSource : ICombatSource
{
    private readonly Capture.NpcapAvailability _npcap = Capture.NpcapAvailability.Detect();
    private readonly Protocol.Aion2Protocol _protocol;
    private readonly Capture.TcpReassembler _reassembler = new();
    private readonly Protocol.Aion2FrameDecoder _decoder;
    private readonly Aion2EntityDirectory _entities = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<DamageEvent> _pending = new();
    private Capture.NpcapCaptureService? _capture;
    private SourceStatus _status = new(SourceState.Idle, "");

    public Aion2PacketCombatSource(Protocol.Aion2Protocol protocol)
    {
        _protocol = protocol;
        _decoder = new Protocol.Aion2FrameDecoder(protocol, _entities);
    }

    public SourceCapabilities Capabilities => SourceCapabilities.ExactIds | SourceCapabilities.Kills | SourceCapabilities.Defense;

    public IEntityDirectory Entities => _entities;

    public string CurrentZone => _decoder.CurrentZone;

    public bool InArena => false;

    public string? ServerFingerprint => _capture?.ServerEndpoint is string endpoint ? $"aion2:{endpoint}" : null;

    public event Action<string, string>? SkillUsed;
    public event Action<SourceStatus>? StatusChanged;

    // Part of the seam, but nothing in a packet stream maps onto them (no chat commands, loot,
    // personal stats or buff narration) - see Capabilities, which is how the UI knows.
#pragma warning disable CS0067
    public event Action<string?, string, string>? CommandReceived;
    public event Action<PersonalStatKind, long>? PersonalStatChanged;
    public event Action<string>? PlayerLoggedIn;
    public event Action<LootEvent>? LootAcquired;
    public event Action<BuffCastEvent>? BuffCast;
#pragma warning restore CS0067

    public void Start()
    {
        if (!_npcap.IsInstalled)
        {
            Report(SourceState.Error, "Aion 2: Npcap is not installed - the meter reads the game's network traffic and needs the Npcap driver (npcap.com). Nothing is captured until it is.");
            return;
        }

        if (!_protocol.IsCalibrated)
        {
            Report(SourceState.Waiting, "Aion 2: the packet layout has not been calibrated for this game version yet - record a fight with \"AionDPS aion2-record\" and update assets/aion2/protocol/opcodes.json.");
            return;
        }

        _capture = new Capture.NpcapCaptureService(_protocol.ServerPorts, OnPayload, OnCaptureStatus);
        _capture.Start();
    }

    public void Stop()
    {
        _capture?.Dispose();
        _capture = null;
    }

    public CombatBatch Poll(bool paused)
    {
        if (_pending.IsEmpty)
        {
            return CombatBatch.Empty;
        }

        var drained = new List<DamageEvent>();
        while (_pending.TryDequeue(out DamageEvent ev))
        {
            drained.Add(ev);
        }

        // Paused time is discarded, not deferred - same tape-recorder rule as the Chat.log source.
        return paused ? CombatBatch.Empty : CombatBatch.DamageOnly(drained);
    }

    public void Dispose() => Stop();

    /// <summary>Feeds one captured segment through reassembly and decoding - the live capture's
    /// callback, and what a recorded fixture is replayed through in the self-checks.</summary>
    public void Ingest(Capture.TcpSegment segment)
    {
        lock (_reassembler)
        {
            foreach (ReadOnlyMemory<byte> frame in _reassembler.Push(segment, _protocol.FrameLayout))
            {
                foreach (DamageEvent ev in _decoder.Decode(frame.Span, segment.Timestamp))
                {
                    _pending.Enqueue(ev);
                }
            }

            foreach ((string actor, string skill) in _decoder.DrainSkillUses())
            {
                SkillUsed?.Invoke(actor, skill);
            }
        }
    }

    private void OnPayload(Capture.TcpSegment segment) => Ingest(segment);

    private void OnCaptureStatus(SourceState state, string message) => Report(state, $"Aion 2: {message}");

    private void Report(SourceState state, string message)
    {
        if (_status.State == state && _status.Message == message)
        {
            return;
        }

        _status = new SourceStatus(state, message);
        StatusChanged?.Invoke(_status);
    }
}
