// Aion 2 server ids and their names, from the game's own text (ServerName_<id>_desc in the client's
// L10NString, checked 2026-10-07; same table as Client/Aion2/Protocol/Aion2Servers.cs). Block 13/23 is
// Europe, its first 22 servers per faction are the European list; every other block has the right
// name but an unknown region and is labelled "Name [id]". An upload from an older client files an
// unnamed server under "aion2:aion-2-server-<id>"; canonicalServer() turns that into the proper name
// so the same server never exists twice.
const ELYOS_13 = ["Siel", "Nezekan", "Vaizel", "Kaisinel", "Yustiel", "Ariel", "Fregion", "Meslamtaeda", "Hithanya", "Nania", "Tahavatha", "Luteros", "Phernos", "Daminu", "Kasaka", "Bakarma", "Tsenka", "Kochi", "Ishtar", "Tiamat", "Gauss", "Lamuatan", "Nathara", "Talisra", "Zumion", "Nahid", "Asahr", "Caelid", "Laveis", "Perion", "Dramata", "Reda", "Auldor", "Vakron", "Narun", "Gartua", "Chloris", "Ione", "Teina", "Dymones", "Bargott", "Atheron", "Ruthilis", "Siliator", "Idris", "Satia", "Estian", "Rahu", "Rhanman", "Hebran", "Urahum", "Lakshmi", "Thamon", "Tiere", "Duduri", "Derkos", "Dundu", "Holyaul", "Poeta", "Verteron"];
const ELYOS_10 = ["Siel", "Nezekan", "Vaizel", "Kaisinel", "Yustiel", "Ariel", "Fregion", "Meslamtaeda", "Hithanya", "Nania", "Tahavatha", "Luteros", "Phernos", "Daminu", "Kasaka", "Bakarma", "Tsenka", "Kochi", "Ishtar", "Tiamat", "Poeta", "Verteron", "Nathara", "Talisra", "Zumion", "Nahid", "Asahr", "Caelid", "Laveis", "Perion", "Dramata", "Reda", "Auldor", "Vakron", "Narun", "Gartua", "Chloris", "Ione", "Teina", "Dymones", "Bargott", "Atheron", "Ruthilis", "Siliator", "Idris", "Satia", "Estian", "Rahu", "Rhanman", "Hebran", "Urahum", "Lakshmi", "Thamon", "Tiere", "Duduri", "Derkos", "Dundu", "Holyaul"];
const ASMO_23 = ["Israphel", "Zikel", "Triniel", "Lumiel", "Marchutan", "Azphel", "Ereshkigal", "Beritra", "Nemon", "Hadala", "Ludra", "Ulgorn", "Munin", "Odar", "Zemurru", "Kromede", "Quai", "Baba", "Fafnir", "Indnath", "Agnita", "Atiel", "Tassin", "Heladrir", "Valdemar", "Lagta", "Gerod", "Urd", "Ecco", "Giselle", "Kashapa", "Stof", "Berk", "Nuakum", "Grisilla", "Santras", "Reuben", "Hugo", "Kraki", "Hystan", "Rathman", "Sigebert", "Nazmun", "Gelcos", "Paton", "Pelleir", "Elvida", "Ketu", "Pydeon", "Notun", "Murute", "Rotan", "Kwapo", "Duanka", "Brok", "Valter", "Purakhi", "Ignus", "Ishalgen", "Altgard"];
const ASMO_20 = ["Israphel", "Zikel", "Triniel", "Lumiel", "Marchutan", "Azphel", "Ereshkigal", "Beritra", "Nemon", "Hadala", "Ludra", "Ulgorn", "Munin", "Odar", "Zemurru", "Kromede", "Quai", "Baba", "Fafnir", "Indnath", "Ishalgen", "Altgard", "Agnita", "Atiel", "Valdemar", "Lagta", "Gerod", "Urd", "Ecco", "Giselle", "Kashapa", "Stof", "Berk", "Nuakum", "Grisilla", "Santras", "Reuben", "Hugo", "Kraki", "Hystan", "Rathman", "Sigebert", "Nazmun", "Gelcos", "Paton", "Pelleir", "Elvida", "Ketu", "Pydeon", "Notun", "Murute", "Rotan", "Kwapo", "Duanka", "Brok", "Valter", "Purakhi", "Ignus"];
const EUROPE_SERVERS_PER_FACTION = 22;

function nameOf(serverId: number): string | null {
  const block = Math.floor(serverId / 100);
  const index = (serverId % 100) - 1;
  const names =
    block === 13 || (block >= 11 && block <= 15 && block !== 10) ? ELYOS_13
    : block === 10 ? ELYOS_10
    : block === 23 || (block >= 21 && block <= 25) ? ASMO_23
    : block === 20 ? ASMO_20
    : null;
  const name = names?.[index];
  if (!name) {
    return null;
  }
  return (block === 13 || block === 23) && index < EUROPE_SERVERS_PER_FACTION ? `Europe - ${name}` : `${name} [${serverId}]`;
}

const slug = (name: string) => name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

export function canonicalServer(fingerprint: string, displayName: string | undefined): { fingerprint: string; displayName: string | undefined } {
  const match = /^aion2:aion-2-server-(\d+)$/.exec(fingerprint);
  const label = match ? nameOf(Number(match[1])) : null;
  if (!label) {
    return { fingerprint, displayName };
  }
  return { fingerprint: `aion2:${slug(label)}`, displayName: label };
}
