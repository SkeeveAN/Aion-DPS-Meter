// Aion 2 server ids and their names, from the game's own text (ServerName_<id>_desc in the client's
// L10NString, checked 2026-10-07). Elyos 1301-1322 and Asmodians 2301-2322 are the 22 servers per
// faction of the European list. An upload from an older client files an unnamed server under
// "aion2:aion-2-server-<id>"; canonicalServer() turns that into the proper name so the same server
// never exists twice.
const NAMES: Record<number, string> = {
  1301: "Siel",
  1302: "Nezekan",
  1303: "Vaizel",
  1304: "Kaisinel",
  1305: "Yustiel",
  1306: "Ariel",
  1307: "Fregion",
  1308: "Meslamtaeda",
  1309: "Hithanya",
  1310: "Nania",
  1311: "Tahavatha",
  1312: "Luteros",
  1313: "Phernos",
  1314: "Daminu",
  1315: "Kasaka",
  1316: "Bakarma",
  1317: "Tsenka",
  1318: "Kochi",
  1319: "Ishtar",
  1320: "Tiamat",
  1321: "Gauss",
  1322: "Lamuatan",
  2301: "Israphel",
  2302: "Zikel",
  2303: "Triniel",
  2304: "Lumiel",
  2305: "Marchutan",
  2306: "Azphel",
  2307: "Ereshkigal",
  2308: "Beritra",
  2309: "Nemon",
  2310: "Hadala",
  2311: "Ludra",
  2312: "Ulgorn",
  2313: "Munin",
  2314: "Odar",
  2315: "Zemurru",
  2316: "Kromede",
  2317: "Quai",
  2318: "Baba",
  2319: "Fafnir",
  2320: "Indnath",
  2321: "Agnita",
  2322: "Atiel",
};

const slug = (name: string) => name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");

export function canonicalServer(fingerprint: string, displayName: string | undefined): { fingerprint: string; displayName: string | undefined } {
  const match = /^aion2:aion-2-server-(\d+)$/.exec(fingerprint);
  const name = match ? NAMES[Number(match[1])] : undefined;
  if (!name) {
    return { fingerprint, displayName };
  }
  const label = `Europe - ${name}`;
  return { fingerprint: `aion2:${slug(label)}`, displayName: label };
}
