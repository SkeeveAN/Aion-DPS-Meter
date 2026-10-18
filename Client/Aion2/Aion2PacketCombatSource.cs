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

    /// <summary>The server ids seen next to player names, with sample names (diagnostic).</summary>
    public IReadOnlyDictionary<int, SortedSet<string>> ServerIdsSeen => _decoder.ServerIdsSeen;
    private readonly Aion2EntityDirectory _entities = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<DamageEvent> _pending = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<ZoneChange> _zones = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<EnrageMark> _enrages = new();
    private readonly string? _adapterId;
    private Capture.NpcapCaptureService? _capture;
    private SourceStatus _status = new(SourceState.Idle, "");

    private static readonly TimeSpan CharacterSaveDelay = TimeSpan.FromSeconds(3);
    private readonly object _saveGate = new();
    private readonly object _saveIo = new();
    private string? _characterStorePath;
    private System.Threading.Timer? _saveTimer;
    private bool _saveDirty;

    public Aion2PacketCombatSource(Protocol.Aion2Protocol protocol, string? adapterId = null, string? ownCharacterName = null, string? characterStorePath = null)
    {
        _adapterId = adapterId;
        _entities.SetConfiguredLocalName(ownCharacterName);
        if (characterStorePath is not null)
        {
            // Data from the last login, so the Character view and the profile upload are never empty
            // after a mid-session start; every fresh record the game sends replaces and re-saves it.
            if (Aion2CharacterStore.Load(characterStorePath) is { } saved)
            {
                _entities.RestoreFrom(saved);
            }

            string inspectedPath = Aion2CharacterStore.InspectedPathFor(characterStorePath);
            _entities.RestoreInspected(Aion2CharacterStore.LoadInspected(inspectedPath));
            _entities.InspectedChanged += () => Aion2CharacterStore.SaveInspected(inspectedPath, _entities.InspectedPlayers());

            // Every record of the login burst and every stat frame reports a change; the file is written once per few seconds instead of
            // once per report (the snapshot is rebuilt and serialized in full each time), and off the capture thread.
            _characterStorePath = characterStorePath;
            _entities.CharacterChanged += _ => ScheduleCharacterSave();
        }
        _entities.CharacterChanged += info =>
        {
            Capture.DebugRecording.NoteCharacter(info.Name);
            if (!info.Restored)
            {
                Aion2Tickets.NoteCharacter(info.Name, info.ServerId, info.ReceivedAt, info.Level); // the tickets of the login list belong to the character named a few seconds later
            }
        };
        _protocol = protocol;
        _decoder = new Protocol.Aion2FrameDecoder(protocol, _entities);
    }

    public SourceCapabilities Capabilities => SourceCapabilities.ExactIds | SourceCapabilities.Kills | SourceCapabilities.Defense;

    public IEntityDirectory Entities => _entities;

    public string CurrentZone => _decoder.CurrentZone;

    /// <summary>The id of the zone the local player is in (0 until the game has sent one).</summary>
    public int CurrentZoneId => _decoder.CurrentZoneId;

    public bool InArena => false;

    public string? ServerFingerprint => _capture?.ServerEndpoint is string endpoint ? $"aion2:{endpoint}" : null;

    public event Action<string, string>? SkillUsed;

    /// <summary>Raised once when the stream alone reveals the local player's name (see
    /// <see cref="Aion2EntityDirectory.LearnedLocalName"/>), so the caller can remember it.</summary>
    public event Action<string>? LocalNameLearned;
    private string? _learnedReported;
    private long _eventsDecoded;
    private DateTime _lastStatusAt = DateTime.MinValue;
    public event Action<SourceStatus>? StatusChanged;

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

        _capture = new Capture.NpcapCaptureService(_protocol.ServerPorts, OnPayload, OnCaptureStatus, _adapterId);
        _capture.Start();
    }

    public void Stop()
    {
        _capture?.Dispose();
        _capture = null;
        FlushCharacterSave(); // what was reported in the last seconds is not lost
    }

    /// <summary>Marks the character data as changed; the first change after a write starts the timer, later ones ride along (so a steady
    /// stream of stat frames still reaches the disk every few seconds).</summary>
    private void ScheduleCharacterSave()
    {
        lock (_saveGate)
        {
            if (_saveDirty)
            {
                return;
            }

            _saveDirty = true;
            _saveTimer ??= new System.Threading.Timer(_ => FlushCharacterSave(), null, System.Threading.Timeout.InfiniteTimeSpan, System.Threading.Timeout.InfiniteTimeSpan);
            _saveTimer.Change(CharacterSaveDelay, System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushCharacterSave()
    {
        if (_characterStorePath is not { } path)
        {
            return;
        }

        lock (_saveGate)
        {
            if (!_saveDirty)
            {
                return;
            }

            _saveDirty = false;
        }

        lock (_saveIo)
        {
            if (_entities.ToSaved() is { } snapshot)
            {
                Aion2CharacterStore.Save(path, snapshot);
            }
        }
    }

    public CombatBatch Poll(bool paused)
    {
        ReportLiveCounters();

        if (_entities.LearnedLocalName is string learned && learned != _learnedReported)
        {
            _learnedReported = learned;
            LocalNameLearned?.Invoke(learned);
        }

        // Zone changes and boss rages are not combat data of the player: they come through while paused too (the session list must stay right).
        List<ZoneChange>? zones = null;
        while (_zones.TryDequeue(out ZoneChange zone))
        {
            (zones ??= new List<ZoneChange>()).Add(zone);
        }

        List<EnrageMark>? enrages = null;
        while (_enrages.TryDequeue(out EnrageMark mark))
        {
            (enrages ??= new List<EnrageMark>()).Add(mark);
        }

        var drained = new List<DamageEvent>();
        while (_pending.TryDequeue(out DamageEvent ev))
        {
            drained.Add(ev);
        }

        if (drained.Count == 0 && zones is null && enrages is null)
        {
            return CombatBatch.Empty;
        }

        // Paused time is discarded, not deferred - tape-recorder rule.
        return new CombatBatch(paused ? Array.Empty<DamageEvent>() : drained, Array.Empty<AvoidEvent>(), Array.Empty<KillEvent>(), zones, enrages);
    }

    public void Dispose()
    {
        Stop();
        lock (_saveGate)
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
    }

    /// <summary>
    /// A live one-line health report for the status bar: once the game server has been seen, how
    /// much has arrived and how much of it decoded. "Capturing ... waiting" for minutes while frames
    /// stay at 0 points at capture/alignment; frames growing with events at 0 points at the layout.
    /// Throttled, since the status line repaints on every change.
    /// </summary>
    private void ReportLiveCounters()
    {
        if (_capture?.ServerEndpoint is not string endpoint || DateTime.UtcNow - _lastStatusAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastStatusAt = DateTime.UtcNow;
        long frames;
        int desyncs;
        lock (_reassembler)
        {
            frames = _reassembler.Frames;
            desyncs = _reassembler.Desyncs;
        }

        int errors = _capture.CallbackErrors;
        Report(SourceState.Connected, $"Aion 2: {endpoint} - {frames:N0} frames, {_eventsDecoded:N0} events, {desyncs:N0} resyncs"
            + (_capture.Dropped > 0 ? $", {_capture.Dropped:N0} dropped" : "")
            + (errors > 0 ? $", {errors:N0} errors ({_capture.LastCallbackError})" : ""));
    }

    /// <summary>Feeds one captured segment through reassembly and decoding - the live capture's
    /// callback, and what a recorded fixture is replayed through in the self-checks.</summary>
    public void Ingest(Capture.TcpSegment segment)
    {
        // Only the server's stream carries combat; the client's small command packets are a
        // different vocabulary and would only risk a false frame.
        if (!segment.FromServer)
        {
            return;
        }

        lock (_reassembler)
        {
            foreach (ReadOnlyMemory<byte> frame in _reassembler.Push(segment, _protocol.FrameLayout, _protocol.SyncOpcodes))
            {
                foreach (DamageEvent ev in _decoder.Decode(frame.Span, segment.Timestamp))
                {
                    _pending.Enqueue(ev);
                    _eventsDecoded++;
                }
            }

            foreach (ZoneChange zone in _decoder.DrainZoneChanges())
            {
                _zones.Enqueue(zone);
                _entities.ForgetFight();
            }

            foreach (EnrageMark mark in _decoder.DrainEnrages())
            {
                _enrages.Enqueue(mark);
            }

            foreach ((string actor, string skill) in _decoder.DrainSkillUses())
            {
                SkillUsed?.Invoke(actor, skill);
            }
        }
    }

    private void OnPayload(Capture.TcpSegment segment)
    {
        Feedback.CaptureRingBuffer.Add(segment); // for "attach recording" in the feedback window
        Capture.DebugRecording.Add(segment); // only while Settings > Debug recording is on
        Ingest(segment);
    }

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
