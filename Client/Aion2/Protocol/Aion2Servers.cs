namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Aion 2 server ids as the game sends them (the two bytes after the own character's name) and the
/// server each one is. Only ids that were confirmed with a real character are listed; any other id is
/// still a perfectly good identity for filing uploads apart, it just has no name yet.
/// </summary>
public static class Aion2Servers
{
    private static readonly Dictionary<int, string> Known = new()
    {
        [1304] = "Europe - Kaisinel", // the user's Elyos character Aahz, 2026-10-01
        [1303] = "Europe - Vaizel", // told by the user (player Boulenbouche, 2026-10-03)
    };

    public static string NameOf(int serverId) => Known.GetValueOrDefault(serverId, $"Aion 2 server {serverId}");
}
