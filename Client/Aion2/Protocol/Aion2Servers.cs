namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Aion 2 server ids as the game sends them (the two bytes after the own character's name) and the
/// server each one is. The names are the game's own text (ServerName_&lt;id&gt;_desc in the client's
/// L10NString, checked 2026-10-07; the English spelling is used, the game spells a few names differently
/// per language): ids 1001-1560 are Elyos, 2001-2560 Asmodians, six blocks each, with the same name at the
/// same place in every block (the first block of each faction differs slightly). Only block 13/23 is
/// confirmed to be Europe (real characters and party-window server tags: 1303/1304 Aahz and Boulenbouche,
/// Xooby [Tri] = 2303, zyxx [Ber] = 2308); the other regions follow aion2.run (see the Regions table).
/// </summary>
public static class Aion2Servers
{
    private static readonly string[] Elyos13 = { "Siel", "Nezekan", "Vaizel", "Kaisinel", "Yustiel", "Ariel", "Fregion", "Meslamtaeda", "Hithanya", "Nania", "Tahavatha", "Luteros", "Phernos", "Daminu", "Kasaka", "Bakarma", "Tsenka", "Kochi", "Ishtar", "Tiamat", "Gauss", "Lamuatan", "Nathara", "Talisra", "Zumion", "Nahid", "Asahr", "Caelid", "Laveis", "Perion", "Dramata", "Reda", "Auldor", "Vakron", "Narun", "Gartua", "Chloris", "Ione", "Teina", "Dymones", "Bargott", "Atheron", "Ruthilis", "Siliator", "Idris", "Satia", "Estian", "Rahu", "Rhanman", "Hebran", "Urahum", "Lakshmi", "Thamon", "Tiere", "Duduri", "Derkos", "Dundu", "Holyaul", "Poeta", "Verteron" };
    private static readonly string[] Elyos10 = { "Siel", "Nezekan", "Vaizel", "Kaisinel", "Yustiel", "Ariel", "Fregion", "Meslamtaeda", "Hithanya", "Nania", "Tahavatha", "Luteros", "Phernos", "Daminu", "Kasaka", "Bakarma", "Tsenka", "Kochi", "Ishtar", "Tiamat", "Poeta", "Verteron", "Nathara", "Talisra", "Zumion", "Nahid", "Asahr", "Caelid", "Laveis", "Perion", "Dramata", "Reda", "Auldor", "Vakron", "Narun", "Gartua", "Chloris", "Ione", "Teina", "Dymones", "Bargott", "Atheron", "Ruthilis", "Siliator", "Idris", "Satia", "Estian", "Rahu", "Rhanman", "Hebran", "Urahum", "Lakshmi", "Thamon", "Tiere", "Duduri", "Derkos", "Dundu", "Holyaul" };
    private static readonly string[] Asmo23 = { "Israphel", "Zikel", "Triniel", "Lumiel", "Marchutan", "Azphel", "Ereshkigal", "Beritra", "Nemon", "Hadala", "Ludra", "Ulgorn", "Munin", "Odar", "Zemurru", "Kromede", "Quai", "Baba", "Fafnir", "Indnath", "Agnita", "Atiel", "Tassin", "Heladrir", "Valdemar", "Lagta", "Gerod", "Urd", "Ecco", "Giselle", "Kashapa", "Stof", "Berk", "Nuakum", "Grisilla", "Santras", "Reuben", "Hugo", "Kraki", "Hystan", "Rathman", "Sigebert", "Nazmun", "Gelcos", "Paton", "Pelleir", "Elvida", "Ketu", "Pydeon", "Notun", "Murute", "Rotan", "Kwapo", "Duanka", "Brok", "Valter", "Purakhi", "Ignus", "Ishalgen", "Altgard" };
    private static readonly string[] Asmo20 = { "Israphel", "Zikel", "Triniel", "Lumiel", "Marchutan", "Azphel", "Ereshkigal", "Beritra", "Nemon", "Hadala", "Ludra", "Ulgorn", "Munin", "Odar", "Zemurru", "Kromede", "Quai", "Baba", "Fafnir", "Indnath", "Ishalgen", "Altgard", "Agnita", "Atiel", "Valdemar", "Lagta", "Gerod", "Urd", "Ecco", "Giselle", "Kashapa", "Stof", "Berk", "Nuakum", "Grisilla", "Santras", "Reuben", "Hugo", "Kraki", "Hystan", "Rathman", "Sigebert", "Nazmun", "Gelcos", "Paton", "Pelleir", "Elvida", "Ketu", "Pydeon", "Notun", "Murute", "Rotan", "Kwapo", "Duanka", "Brok", "Valter", "Purakhi", "Ignus" };

    // The region is the digit after the faction digit: 1 NA East, 2 NA West, 3 Europe, 4 LATAM, 5 Asia
    // (aion2.run, matching the client's six id blocks; only Europe is confirmed with real characters so
    // far - the game server address every upload carries will confirm the others). The first N servers of a
    // block are the live ones of that region; block 10 and the ids beyond are labelled "Name [id]".
    private static readonly Dictionary<int, (string Name, int PerFaction)> Regions = new()
    {
        [1] = ("NA East", 8),
        [2] = ("NA West", 5),
        [3] = ("Europe", 22),
        [4] = ("LATAM", 6),
        [5] = ("Asia", 8),
    };

    public static string NameOf(int serverId)
    {
        int faction = serverId / 1000;
        int block = serverId / 100 % 10;
        int index = serverId % 100 - 1;
        if (faction is not (1 or 2) || index < 0 || block > 5)
        {
            return $"Aion 2 server {serverId}";
        }

        string[] names = faction == 1 ? (block == 0 ? Elyos10 : Elyos13) : (block == 0 ? Asmo20 : Asmo23);
        if (index >= names.Length)
        {
            return $"Aion 2 server {serverId}";
        }

        return Regions.TryGetValue(block, out var region) && index < region.PerFaction
            ? $"{region.Name} - {names[index]}"
            : $"{names[index]} [{serverId}]";
    }
}
