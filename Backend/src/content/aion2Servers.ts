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
// of a block are the live ones of that region; the ids beyond are labelled "Name [id]".
const REGIONS: Record<number, { name: string; perFaction: number }> = {
  // Block 10/20 is the Taiwan version (NC's own list at tw.ncsoft.com: 1001-1018 / 2001-2018, same names in the same order as the first 18 of these lists; shown by
  // the players who grouped with a client on Taiwan's game servers, 24.10.2026). Korea uses the same ids (1001-1021 / 2001-2021), so the number alone cannot tell the two
  // apart: a Korean player would be filed under Taiwan too. No Korean upload has been seen yet.
  0: { name: "Taiwan", perFaction: 18 },
  1: { name: "NA East", perFaction: 9 },
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

/**
 * Korea numbers its 21 servers per faction like Taiwan (1001-1021 / 2001-2021, the same names in the same order: NC's Korean search names 1001 시엘, 1004 카이시넬, 2004 루미엘, 1021 포에타,
 * 2021 이스할겐, checked live 31.10.2026), so the id alone cannot tell the two apart. A Korean server is told only by the region NC's search found the character in; its rows are
 * labelled "Korea - Name" and never made from an id on its own (an upload's id 10xx stays Taiwan).
 */
export const KOREA_PER_FACTION = 21;
export function koreaNameOf(serverId: number): string | null {
  const faction = Math.floor(serverId / 1000);
  const index = (serverId % 1000) - 1;
  if ((faction !== 1 && faction !== 2) || index < 0 || index >= KOREA_PER_FACTION) {
    return null;
  }
  return `Korea - ${(faction === 1 ? ELYOS_10 : ASMO_20)[index]}`;
}

/** The fingerprint and display name of a Korean server (same shape as canonicalServer's). */
export function koreaServer(serverId: number): { fingerprint: string; displayName: string } | null {
  const label = koreaNameOf(serverId);
  return label ? { fingerprint: `aion2:${slug(label)}`, displayName: label } : null;
}

const slug = (name: string) => name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

export function canonicalServer(fingerprint: string, displayName: string | undefined): { fingerprint: string; displayName: string | undefined } {
  // also the "Name [id]" rows older clients filed a server under before its region was known ("aion2:hithanya-1109")
  const match = /^aion2:(?:aion-2-server-(\d+)|[a-z0-9-]+-(\d{4}))$/.exec(fingerprint);
  const label = match ? nameOf(Number(match[1] ?? match[2])) : null;
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
  if (m?.[1] === "Korea") {
    const elyos = ELYOS_10.indexOf(m[2]);
    if (elyos >= 0 && elyos < KOREA_PER_FACTION) {
      return { id: 1001 + elyos, faction: "Elyos" };
    }
    const asmo = ASMO_20.indexOf(m[2]);
    return asmo >= 0 && asmo < KOREA_PER_FACTION ? { id: 2001 + asmo, faction: "Asmodian" } : null;
  }
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
