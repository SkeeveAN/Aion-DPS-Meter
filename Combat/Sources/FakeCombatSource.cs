using AionDPS.ChatLog;

namespace AionDPS.Combat.Sources;

/// <summary>
/// A scriptable source for self-checks and demo data: whatever is queued with
/// <see cref="Enqueue"/> comes back from the next <see cref="Poll"/>. Names are registered through
/// the same <see cref="PlayerNameRegistry"/> the Chat.log path uses, so ids behave identically.
/// </summary>
public sealed class FakeCombatSource : ICombatSource
{
    private readonly Queue<CombatBatch> _pending = new();
    private readonly PlayerNameRegistry _names = new();
    private readonly Directory _entities;

    public FakeCombatSource(string localPlayerName = "You", SourceCapabilities capabilities = SourceCapabilities.None)
    {
        LocalPlayerName = localPlayerName;
        Capabilities = capabilities;
        _entities = new Directory(this);
        _names.GetOrAssignId(localPlayerName);
    }

    public string LocalPlayerName { get; }

    public SourceCapabilities Capabilities { get; }

    public IEntityDirectory Entities => _entities;

    public string CurrentZone { get; set; } = "";

    public bool InArena { get; set; }

    public event Action<string, string>? SkillUsed;
    public event Action<string?, string, string>? CommandReceived;
    public event Action<PersonalStatKind, long>? PersonalStatChanged;
    public event Action<string>? PlayerLoggedIn;
    public event Action<LootEvent>? LootAcquired;
    public event Action<BuffCastEvent>? BuffCast;
    public event Action<SourceStatus>? StatusChanged;

    public int IdOf(string name) => _names.GetOrAssignId(name);

    public void Enqueue(CombatBatch batch) => _pending.Enqueue(batch);

    public void Enqueue(params DamageEvent[] damage) => _pending.Enqueue(CombatBatch.DamageOnly(damage));

    public void RaiseSkillUsed(string actor, string skill) => SkillUsed?.Invoke(actor, skill);

    public void RaiseCommand(string? sender, string command, string argument) => CommandReceived?.Invoke(sender, command, argument);

    public void RaisePersonalStat(PersonalStatKind kind, long delta) => PersonalStatChanged?.Invoke(kind, delta);

    public void RaisePlayerLoggedIn(string name) => PlayerLoggedIn?.Invoke(name);

    public void RaiseLoot(LootEvent evt) => LootAcquired?.Invoke(evt);

    public void RaiseBuffCast(BuffCastEvent evt) => BuffCast?.Invoke(evt);

    public void RaiseStatus(SourceState state, string message) => StatusChanged?.Invoke(new SourceStatus(state, message));

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public CombatBatch Poll(bool paused)
    {
        if (_pending.Count == 0)
        {
            return CombatBatch.Empty;
        }

        CombatBatch batch = _pending.Dequeue();
        return paused ? CombatBatch.Empty : batch;
    }

    public void Dispose()
    {
    }

    private sealed class Directory : IEntityDirectory
    {
        private readonly FakeCombatSource _owner;

        public Directory(FakeCombatSource owner) => _owner = owner;

        public string? NameFor(int id) => _owner._names.NameFor(id);

        public int GetOrAssignId(string name) => _owner._names.GetOrAssignId(name);

        public int LocalPlayerId => _owner._names.GetOrAssignId(_owner.LocalPlayerName);

        public bool IsLocalPlayer(int id) => id == LocalPlayerId;
    }
}
