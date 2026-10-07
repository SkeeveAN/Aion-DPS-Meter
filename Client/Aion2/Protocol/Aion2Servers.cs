namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Aion 2 server ids as the game sends them (the two bytes after the own character's name) and the
/// server each one is. The names are the game's own text (ServerName_&lt;id&gt;_desc in the client's
/// L10NString, checked 2026-10-07): ids 1001-1560 are Elyos, 2001-2560 Asmodians, six blocks each,
/// with the same name at the same place in every block (the first block of each faction differs
/// slightly). Only block 13/23 is confirmed to be Europe (real characters and party-window server
/// tags: 1303/1304 Aahz and Boulenbouche, Xooby [Tri] = 2303, zyxx [Ber] = 2308); its first 22 servers
/// are the European list. For every other id the name is right but the region is not known, so it
/// is shown as "Name [id]" and never as "Europe". The same id can exist in several regions.
/// </summary>
public static class Aion2Servers
{
    private static readonly string[] Elyos13 = { "Siel", "Nezekan", "Vaizel", "Kaisinel", "Yustiel", "Ariel", "Fregion", "Meslamtaeda", "Hithanya", "Nania", "Tahavatha", "Luteros", "Phernos", "Daminu", "Kasaka", "Bakarma", "Tsenka", "Kochi", "Ishtar", "Tiamat", "Gauss", "Lamuatan", "Nathara", "Talisra", "Zumion", "Nahid", "Asahr", "Caelid", "Laveis", "Perion", "Dramata", "Reda", "Auldor", "Vakron", "Narun", "Gartua", "Chloris", "Ione", "Teina", "Dymones", "Bargott", "Atheron", "Ruthilis", "Siliator", "Idris", "Satia", "Estian", "Rahu", "Rhanman", "Hebran", "Urahum", "Lakshmi", "Thamon", "Tiere", "Duduri", "Derkos", "Dundu", "Holyaul", "Poeta", "Verteron" };
    private static readonly string[] Elyos10 = { "Siel", "Nezekan", "Vaizel", "Kaisinel", "Yustiel", "Ariel", "Fregion", "Meslamtaeda", "Hithanya", "Nania", "Tahavatha", "Luteros", "Phernos", "Daminu", "Kasaka", "Bakarma", "Tsenka", "Kochi", "Ishtar", "Tiamat", "Poeta", "Verteron", "Nathara", "Talisra", "Zumion", "Nahid", "Asahr", "Caelid", "Laveis", "Perion", "Dramata", "Reda", "Auldor", "Vakron", "Narun", "Gartua", "Chloris", "Ione", "Teina", "Dymones", "Bargott", "Atheron", "Ruthilis", "Siliator", "Idris", "Satia", "Estian", "Rahu", "Rhanman", "Hebran", "Urahum", "Lakshmi", "Thamon", "Tiere", "Duduri", "Derkos", "Dundu", "Holyaul" };
    private static readonly string[] Asmo23 = { "Israphel", "Zikel", "Triniel", "Lumiel", "Marchutan", "Azphel", "Ereshkigal", "Beritra", "Nemon", "Hadala", "Ludra", "Ulgorn", "Munin", "Odar", "Zemurru", "Kromede", "Quai", "Baba", "Fafnir", "Indnath", "Agnita", "Atiel", "Tassin", "Heladrir", "Valdemar", "Lagta", "Gerod", "Urd", "Ecco", "Giselle", "Kashapa", "Stof", "Berk", "Nuakum", "Grisilla", "Santras", "Reuben", "Hugo", "Kraki", "Hystan", "Rathman", "Sigebert", "Nazmun", "Gelcos", "Paton", "Pelleir", "Elvida", "Ketu", "Pydeon", "Notun", "Murute", "Rotan", "Kwapo", "Duanka", "Brok", "Valter", "Purakhi", "Ignus", "Ishalgen", "Altgard" };
    private static readonly string[] Asmo20 = { "Israphel", "Zikel", "Triniel", "Lumiel", "Marchutan", "Azphel", "Ereshkigal", "Beritra", "Nemon", "Hadala", "Ludra", "Ulgorn", "Munin", "Odar", "Zemurru", "Kromede", "Quai", "Baba", "Fafnir", "Indnath", "Ishalgen", "Altgard", "Agnita", "Atiel", "Valdemar", "Lagta", "Gerod", "Urd", "Ecco", "Giselle", "Kashapa", "Stof", "Berk", "Nuakum", "Grisilla", "Santras", "Reuben", "Hugo", "Kraki", "Hystan", "Rathman", "Sigebert", "Nazmun", "Gelcos", "Paton", "Pelleir", "Elvida", "Ketu", "Pydeon", "Notun", "Murute", "Rotan", "Kwapo", "Duanka", "Brok", "Valter", "Purakhi", "Ignus" };

    private const int EuropeServersPerFaction = 22;

    public static string NameOf(int serverId)
    {
        int block = serverId / 100;
        int index = serverId % 100 - 1;
        string[]? names = block switch
        {
            13 => Elyos13,
            23 => Asmo23,
            10 => Elyos10,
            20 => Asmo20,
            >= 11 and <= 15 => Elyos13,
            >= 21 and <= 25 => Asmo23,
            _ => null,
        };
        if (names is null || index < 0 || index >= names.Length)
        {
            return $"Aion 2 server {serverId}";
        }
        return (block is 13 or 23) && index < EuropeServersPerFaction ? $"Europe - {names[index]}" : $"{names[index]} [{serverId}]";
    }
}
