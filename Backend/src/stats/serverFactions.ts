// The faction of an Aion 2 server by its name (the part after "Europe - "), for the servers the catalog has no faction for.
// Same lists as the client's Aion2Servers (Elyos servers and Asmodian servers are separate worlds).
const ELYOS = new Set(["Ariel", "Asahr", "Atheron", "Auldor", "Bakarma", "Bargott", "Caelid", "Chloris", "Daminu", "Derkos", "Dramata", "Duduri", "Dundu", "Dymones", "Estian", "Fregion", "Gartua", "Gauss", "Hebran", "Hithanya", "Holyaul", "Idris", "Ione", "Ishtar", "Kaisinel", "Kasaka", "Kochi", "Lakshmi", "Lamuatan", "Laveis", "Luteros", "Meslamtaeda", "Nahid", "Nania", "Narun", "Nathara", "Nezekan", "Perion", "Phernos", "Poeta", "Rahu", "Reda", "Rhanman", "Ruthilis", "Satia", "Siel", "Siliator", "Tahavatha", "Talisra", "Teina", "Thamon", "Tiamat", "Tiere", "Tsenka", "Urahum", "Vaizel", "Vakron", "Verteron", "Yustiel", "Zumion"]);
const ASMODIAN = new Set(["Agnita", "Altgard", "Atiel", "Azphel", "Baba", "Beritra", "Berk", "Brok", "Duanka", "Ecco", "Elvida", "Ereshkigal", "Fafnir", "Gelcos", "Gerod", "Giselle", "Grisilla", "Hadala", "Heladrir", "Hugo", "Hystan", "Ignus", "Indnath", "Ishalgen", "Israphel", "Kashapa", "Ketu", "Kraki", "Kromede", "Kwapo", "Lagta", "Ludra", "Lumiel", "Marchutan", "Munin", "Murute", "Nazmun", "Nemon", "Notun", "Nuakum", "Odar", "Paton", "Pelleir", "Purakhi", "Pydeon", "Quai", "Rathman", "Reuben", "Rotan", "Santras", "Sigebert", "Stof", "Tassin", "Triniel", "Ulgorn", "Urd", "Valdemar", "Valter", "Zemurru", "Zikel"]);

export function factionOfServerName(displayName: string | null | undefined): string {
  const name = (displayName ?? "").split(" - ").pop()?.trim() ?? "";
  return ELYOS.has(name) ? "Elyos" : ASMODIAN.has(name) ? "Asmodian" : "";
}
