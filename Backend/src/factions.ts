// In Aion 2 a server has one faction: Elyos and Asmodians never share a server, so a player's faction follows from the server.
// The faction of a server by its name (the part after "Europe - "): the same lists as the client's Aion2Servers.
const ELYOS = new Set(["Ariel", "Asahr", "Atheron", "Auldor", "Bakarma", "Bargott", "Caelid", "Chloris", "Daminu", "Derkos", "Dramata", "Duduri", "Dundu", "Dymones", "Estian", "Fregion", "Gartua", "Gauss", "Hebran", "Hithanya", "Holyaul", "Idris", "Ione", "Ishtar", "Kaisinel", "Kasaka", "Kochi", "Lakshmi", "Lamuatan", "Laveis", "Luteros", "Meslamtaeda", "Nahid", "Nania", "Narun", "Nathara", "Nezekan", "Perion", "Phernos", "Poeta", "Rahu", "Reda", "Rhanman", "Ruthilis", "Satia", "Siel", "Siliator", "Tahavatha", "Talisra", "Teina", "Thamon", "Tiamat", "Tiere", "Tsenka", "Urahum", "Vaizel", "Vakron", "Verteron", "Yustiel", "Zumion"]);
const ASMODIAN = new Set(["Agnita", "Altgard", "Atiel", "Azphel", "Baba", "Beritra", "Berk", "Brok", "Duanka", "Ecco", "Elvida", "Ereshkigal", "Fafnir", "Gelcos", "Gerod", "Giselle", "Grisilla", "Hadala", "Heladrir", "Hugo", "Hystan", "Ignus", "Indnath", "Ishalgen", "Israphel", "Kashapa", "Ketu", "Kraki", "Kromede", "Kwapo", "Lagta", "Ludra", "Lumiel", "Marchutan", "Munin", "Murute", "Nazmun", "Nemon", "Notun", "Nuakum", "Odar", "Paton", "Pelleir", "Purakhi", "Pydeon", "Quai", "Rathman", "Reuben", "Rotan", "Santras", "Sigebert", "Stof", "Tassin", "Triniel", "Ulgorn", "Urd", "Valdemar", "Valter", "Zemurru", "Zikel"]);

export function factionOfServerName(displayName: string | null | undefined): string {
  const name = (displayName ?? "").split(" - ").pop()?.trim() ?? "";
  return ELYOS.has(name) ? "Elyos" : ASMODIAN.has(name) ? "Asmodian" : "";
}

/** A row keeps the faction the client sent (it knows each player's own server); only a row without one gets the faction of its server.
 *  The stored server is the uploader's for everybody, and matched worlds (Kaisinel <-> Lumiel) mix both factions, so it can't override. */
export function withServerFaction<T extends { serverName?: string | null; faction?: string | null }>(row: T): T & { faction: string } {
  return { ...row, faction: row.faction || factionOfServerName(row.serverName) || "" };
}
