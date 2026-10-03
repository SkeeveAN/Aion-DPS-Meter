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
        // 1303 and 1304 were confirmed with real characters (Boulenbouche, Aahz); the rest of the two
        // blocks of eight is the list the user supplied on 2026-10-03, matched to the ids seen next to
        // player names in the recordings. The id says nothing about a character's faction here.
        [1301] = "Europe - Siel",
        [1302] = "Europe - Nezekan",
        [1303] = "Europe - Vaizel",
        [1304] = "Europe - Kaisinel",
        [1305] = "Europe - Yustiel",
        [1306] = "Europe - Ariel",
        [1307] = "Europe - Fregion",
        [1308] = "Europe - Meslamtaeda",
        [2301] = "Europe - Israphel",
        [2302] = "Europe - Zikel",
        [2303] = "Europe - Triniel",
        [2304] = "Europe - Lumiel",
        [2305] = "Europe - Azphel",
        [2306] = "Europe - Ereshkigal",
        [2307] = "Europe - Beritra",
        [2308] = "Europe - Marchutan",
    };

    public static string NameOf(int serverId) => Known.GetValueOrDefault(serverId, $"Aion 2 server {serverId}");
}
