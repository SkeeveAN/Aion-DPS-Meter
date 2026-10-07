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
        // The names are the game's own text (ServerName_<id>_desc in the client's L10NString, checked 2026-10-07).
        // Elyos 1301-1322 and Asmodians 2301-2322 are the 22 servers per faction of the official list; the
        // client text has more names per block (up to 60) that belong to no live server. Earlier confirmed
        // with real characters: 1303/1304 (Aahz, Boulenbouche), Xooby [Tri] = 2303, zyxx [Ber] = 2308.
        // The same ids are reused by the other regions (NA, Asia, ...): the id alone does not say the region.
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
        [1310] = "Europe - Nania",
        [1311] = "Europe - Tahavatha",
        [1312] = "Europe - Luteros",
        [1313] = "Europe - Phernos",
        [1314] = "Europe - Daminu",
        [1315] = "Europe - Kasaka",
        [1316] = "Europe - Bakarma",
        [1317] = "Europe - Tsenka",
        [1318] = "Europe - Kochi",
        [1319] = "Europe - Ishtar",
        [1320] = "Europe - Tiamat",
        [1321] = "Europe - Gauss",
        [1322] = "Europe - Lamuatan",
        [2301] = "Europe - Israphel",
        [2302] = "Europe - Zikel",
        [2303] = "Europe - Triniel",
        [2304] = "Europe - Lumiel",
        [2305] = "Europe - Marchutan",
        [2306] = "Europe - Azphel",
        [2307] = "Europe - Ereshkigal",
        [2308] = "Europe - Beritra",
        [2309] = "Europe - Nemon",
        [2310] = "Europe - Hadala",
        [2311] = "Europe - Ludra",
        [2312] = "Europe - Ulgorn",
        [2313] = "Europe - Munin",
        [2314] = "Europe - Odar",
        [2315] = "Europe - Zemurru",
        [2316] = "Europe - Kromede",
        [2317] = "Europe - Quai",
        [2318] = "Europe - Baba",
        [2319] = "Europe - Fafnir",
        [2320] = "Europe - Indnath",
        [2321] = "Europe - Agnita",
        [2322] = "Europe - Atiel",
    };

    public static string NameOf(int serverId) => Known.GetValueOrDefault(serverId, $"Aion 2 server {serverId}");
}
