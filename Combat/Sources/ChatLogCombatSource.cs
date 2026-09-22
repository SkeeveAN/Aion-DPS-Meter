using System.IO;
using AionDPS.ChatLog;

namespace AionDPS.Combat.Sources;

/// <summary>
/// The Chat.log input behind <see cref="ICombatSource"/>: a <see cref="ChatLogParser"/> plus a
/// <see cref="ChatLogTailer"/> over the configured install's Chat.log. The tailer is created lazily
/// on the first poll that finds the file, so a client that has never had chat logging enabled is
/// picked up the moment it starts writing, and the parser's own events are forwarded so MainWindow
/// subscribes once to this object rather than to every parser instance
/// (<see cref="ReloadFromDisk"/> swaps the parser).
/// </summary>
public sealed class ChatLogCombatSource : ICombatSource
{
    private readonly string _path;
    private ChatLogParser _parser;
    private ChatLogTailer? _tailer;
    private readonly Directory _entities;
    private bool _forwardCommands = true;
    private SourceState _reportedState = SourceState.Idle;

    public ChatLogCombatSource(string chatLogPath)
    {
        _path = chatLogPath;
        _parser = new ChatLogParser();
        _entities = new Directory(this);
        Subscribe(_parser);
    }

    public string ChatLogPath => _path;

    public SourceCapabilities Capabilities =>
        SourceCapabilities.Loot | SourceCapabilities.ChatCommands | SourceCapabilities.PersonalStats
        | SourceCapabilities.Buffs | SourceCapabilities.Reparse;

    public IEntityDirectory Entities => _entities;

    public string CurrentZone => _parser.CurrentZone;

    public bool InArena => _parser.InArena;

    public event Action<string, string>? SkillUsed;
    public event Action<string?, string, string>? CommandReceived;
    public event Action<PersonalStatKind, long>? PersonalStatChanged;
    public event Action<string>? PlayerLoggedIn;
    public event Action<LootEvent>? LootAcquired;
    public event Action<BuffCastEvent>? BuffCast;
    public event Action<SourceStatus>? StatusChanged;

    public void Start()
    {
        // Explicit rule from the user: recording must never look into the past, so the tailer
        // seeks to the CURRENT end of Chat.log when it is created (see ChatLogTailer) - both here
        // and when the file appears later.
        TryCreateTailer();
    }

    public void Stop() => _tailer = null;

    public CombatBatch Poll(bool paused)
    {
        if (_tailer is null && !TryCreateTailer())
        {
            return CombatBatch.Empty;
        }

        List<DamageEvent> events = _tailer!.Poll(paused);
        return events.Count == 0 ? CombatBatch.Empty : CombatBatch.DamageOnly(events);
    }

    /// <summary>
    /// Explicit, user-triggered exception to the tailer's "never look into the past" rule: parses
    /// the whole file with a brand-new parser (so nothing the live tailer already ingested is
    /// counted twice) and continues live tailing from the file's new end. Chat COMMANDS are not
    /// replayed - ".ui"/".pause"/… from past sessions are control signals, not data to recover
    /// (found the hard way: an old ".ui" flipped the window into overlay mode mid-reload). The
    /// parser is swapped before parsing so handlers that resolve names during the replay already
    /// see the new registry.
    /// </summary>
    public List<DamageEvent> ReloadFromDisk()
    {
        var parser = new ChatLogParser();
        _forwardCommands = false;
        Subscribe(parser);
        _parser = parser;

        List<DamageEvent> events = parser.ParseFile(_path);

        _forwardCommands = true;
        _tailer = new ChatLogTailer(_path, parser);
        return events;
    }

    public void Dispose() => Stop();

    private bool TryCreateTailer()
    {
        if (!File.Exists(_path))
        {
            Report(SourceState.Waiting, $"Waiting for Chat.log at {_path}");
            return false;
        }

        _tailer = new ChatLogTailer(_path, _parser);
        Report(SourceState.Connected, $"Reading {_path}");
        return true;
    }

    private void Report(SourceState state, string message)
    {
        if (state == _reportedState)
        {
            return;
        }

        _reportedState = state;
        StatusChanged?.Invoke(new SourceStatus(state, message));
    }

    private void Subscribe(ChatLogParser parser)
    {
        parser.SkillUsed += (actor, skill) => SkillUsed?.Invoke(actor, skill);
        parser.CommandReceived += (sender, command, argument) =>
        {
            if (_forwardCommands)
            {
                CommandReceived?.Invoke(sender, command, argument);
            }
        };
        parser.PersonalStatChanged += (kind, delta) => PersonalStatChanged?.Invoke(kind, delta);
        parser.PlayerLoggedIn += name => PlayerLoggedIn?.Invoke(name);
        parser.LootAcquired += evt => LootAcquired?.Invoke(evt);
        parser.BuffCast += evt => BuffCast?.Invoke(evt);
    }

    /// <summary>Chat.log names the local player "You" and never by name, so that literal is the
    /// local id (see PlayerNameRegistry's own remarks on the two-clients limitation).</summary>
    private sealed class Directory : IEntityDirectory
    {
        private readonly ChatLogCombatSource _owner;

        public Directory(ChatLogCombatSource owner) => _owner = owner;

        public string? NameFor(int id) => _owner._parser.Names.NameFor(id);

        public int GetOrAssignId(string name) => _owner._parser.Names.GetOrAssignId(name);

        public int LocalPlayerId => _owner._parser.Names.GetOrAssignId("You");

        public bool IsLocalPlayer(int id) => _owner._parser.Names.NameFor(id) == "You";
    }
}
