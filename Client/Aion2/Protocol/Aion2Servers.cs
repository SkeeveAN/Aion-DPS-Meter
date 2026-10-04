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
        // The nine early-access servers per faction, in the order the official list gives them, one id
        // each: Elyos 1301-1309, Asmodians 2301-2309. Confirmed with real characters: 1303/1304 (Aahz,
        // Boulenbouche), and the server tags of the party window - Xooby [Tri] is 2303 (Triniel), zyxx
        // [Ber] is 2308 (Beritra), which also showed that 2305-2308 had been listed in the wrong order
        // (Marchutan is 2305, not 2308). 1309 (Hithanya) and 2309 (Nemon) follow from the same order.
        // The id says nothing about a character's faction here.
        [1301] = "Europe - Siel",
        [1302] = "Europe - Nezekan",
        [1303] = "Europe - Vaizel",
        [1304] = "Europe - Kaisinel",
        [1305] = "Europe - Yustiel",
        [1306] = "Europe - Ariel",
        [1307] = "Europe - Fregion",
        [1308] = "Europe - Meslamtaeda",
        [1309] = "Europe - Hithanya",
        [2301] = "Europe - Israphel",
        [2302] = "Europe - Zikel",
        [2303] = "Europe - Triniel",
        [2304] = "Europe - Lumiel",
        [2305] = "Europe - Marchutan",
        [2306] = "Europe - Azphel",
        [2307] = "Europe - Ereshkigal",
        [2308] = "Europe - Beritra",
        [2309] = "Europe - Nemon",
    };

    public static string NameOf(int serverId) => Known.GetValueOrDefault(serverId, $"Aion 2 server {serverId}");
}
