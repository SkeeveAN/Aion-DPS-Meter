using AionDPS.Combat.Sources;

namespace AionDPS.Aion2;

/// <summary>Aion 2 frames carry the game's own object ids, so this maps those to names as nickname
/// frames reveal them. The local player is whichever id the session frame names. Names that never
/// came with a game id (hand-entered characters) get synthetic negative ids so they can never
/// collide with a real object id.</summary>
public sealed class Aion2EntityDirectory : IEntityDirectory
{
    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<string, int> _ids = new();

    public int LocalPlayerId { get; internal set; } = -1;

    public string? NameFor(int id) => _names.GetValueOrDefault(id);

    public int GetOrAssignId(string name)
    {
        if (_ids.TryGetValue(name, out int id))
        {
            return id;
        }

        id = -(_ids.Count + 2);
        Register(id, name);
        return id;
    }

    public bool IsLocalPlayer(int id) => id == LocalPlayerId;

    public void Register(int id, string name)
    {
        _names[id] = name;
        _ids[name] = id;
    }
}
