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
// The region is the digit after the faction digit: 1 NA East, 2 NA West, 3 Europe, 4 LATAM, 5 Asia
// (the client's six id blocks; the live servers per region are the pairs of NCSOFT's launch notice "Server Matchmaking":
// Europe 18, NA East 8, NA West 5, LATAM 6, Asia 5; the matching of an Elyos with an Asmodian server into one world changes from time
// to time and is not used anywhere - a player's faction is that of his own server). The first N servers
// of a block are the live ones of that region; block 10 and the ids beyond are labelled "Name [id]".
const REGIONS: Record<number, { name: string; perFaction: number }> = {
  1: { name: "NA East", perFaction: 8 },
  2: { name: "NA West", perFaction: 5 },
  3: { name: "Europe", perFaction: 23 },
  4: { name: "LATAM", perFaction: 6 },
  5: { name: "Asia", perFaction: 10 },
};

export function nameOf(serverId: number): string | null {
  const faction = Math.floor(serverId / 1000);
  const block = Math.floor(serverId / 100) % 10;
  const index = (serverId % 100) - 1;
  if ((faction !== 1 && faction !== 2) || index < 0) {
    return null;
  }
  const names = faction === 1 ? (block === 0 ? ELYOS_10 : ELYOS_13) : block === 0 ? ASMO_20 : ASMO_23;
  const name = names[index];
  if (!name || block > 5) {
    return null;
  }
  const region = REGIONS[block];
  return region && index < region.perFaction ? `${region.name} - ${name}` : `${name} [${serverId}]`;
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

const BLOCK_OF_REGION: Record<string, number> = Object.fromEntries(Object.entries(REGIONS).map(([block, r]) => [r.name, Number(block)]));

/**
 * NC's id of a live server of any region by its display name ("Europe - Kaisinel" -> 1304, "NA East - Nezekan" -> 1102) and the faction
 * of that server; null for unlabelled ("Name [id]") or unknown names.
 */
export function serverOfName(displayName: string | null | undefined): { id: number; faction: "Elyos" | "Asmodian" } | null {
  const m = /^(.+?) - (.+)$/.exec((displayName ?? "").trim());
  const block = m ? BLOCK_OF_REGION[m[1]] : undefined;
  if (!m || block === undefined) {
    return null;
  }
  const perFaction = REGIONS[block].perFaction;
  const elyos = ELYOS_13.indexOf(m[2]);
  if (elyos >= 0 && elyos < perFaction) {
    return { id: 1000 + block * 100 + 1 + elyos, faction: "Elyos" };
  }
  const asmo = ASMO_23.indexOf(m[2]);
  return asmo >= 0 && asmo < perFaction ? { id: 2000 + block * 100 + 1 + asmo, faction: "Asmodian" } : null;
}

/** The European servers only (see serverOfName). */
export function europeServerOf(displayName: string | null | undefined): { id: number; faction: "Elyos" | "Asmodian" } | null {
  return /^Europe - /.test((displayName ?? "").trim()) ? serverOfName(displayName) : null;
}
