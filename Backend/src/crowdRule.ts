import { and, eq, isNotNull } from "drizzle-orm";
import { db } from "./db/client.js";
import { bosses, encounterParticipants, encounters, instances, players, serverMatchmaking, uploads } from "./db/schema.js";
import { nameOf, serverOfName } from "./content/aion2Servers.js";
import { regionOfServerName, riftOpen } from "./riftHours.js";

// The world boss rule, applied to the fights already filed (crowdResolve.ts applies it to an upload on its way in).
// Who can be on the map of a world boss kill:
//  - a field boss of a home map (Verteron = Elyos, Altgard = Asmodian) with the uploader of that faction and no rift hour open: only the uploader's own server;
//  - the Abyss (always) and any kill in a rift hour (or an uploader of the other faction): the uploader's server and the one NC's matchmaking paired it with at that time.
// A character filed under any other server than these in such a fight has a wrong server.

const HOME_MAP_FACTION: Record<string, number> = { Verteron: 1, Altgard: 2 };

/** The server the matchmaking paired with `ncServerId` at `at` (epoch ms), as an NC server id; null when no pairing was on record then. */
export function partnerServerAt(ncServerId: number, at: number): number | null {
  const own = nameOf(ncServerId);
  const m = own === null ? null : /^(.+?) - (.+)$/.exec(own);
  if (!m) {
    return null;
  }
  const rows = db.select().from(serverMatchmaking).where(eq(serverMatchmaking.region, m[1])).all();
  const row = rows.find((r) => r.validFrom <= at && (r.validTo === null || at < r.validTo) && (r.elyosServer === m[2] || r.asmodianServer === m[2]));
  if (!row) {
    return null;
  }
  return serverOfName(`${m[1]} - ${row.elyosServer === m[2] ? row.asmodianServer : row.elyosServer}`)?.id ?? null;
}

/**
 * The servers a participant of this fight can be on, as NC server ids. `pairKnown` is false when no pairing was on record at the time (the other faction cannot be
 * judged then). Null for a fight that is no world boss or has no uploader with a server.
 */
export function allowedServersOfEncounter(encounterId: number): { servers: Set<number>; pairKnown: boolean } | null {
  const fight = db
    .select({ startedAt: encounters.startedAt, endedAt: encounters.endedAt, instanceName: instances.name, category: instances.category })
    .from(encounters)
    .innerJoin(bosses, eq(bosses.id, encounters.bossId))
    .innerJoin(instances, eq(instances.id, bosses.instanceId))
    .where(eq(encounters.id, encounterId))
    .get();
  if (!fight || fight.category !== "worldboss") {
    return null;
  }
  const homes = db
    .selectDistinct({ id: uploads.clientServerId })
    .from(uploads)
    .where(and(eq(uploads.matchedEncounterId, encounterId), isNotNull(uploads.clientServerId)))
    .all()
    .map((r) => r.id!);
  if (homes.length === 0) {
    return null;
  }
  const start = Date.parse(fight.startedAt);
  const end = Date.parse(fight.endedAt);
  const homeMap = HOME_MAP_FACTION[fight.instanceName];
  const servers = new Set<number>();
  let pairKnown = true;
  for (const home of homes) {
    servers.add(home);
    // the rifts follow the local time of the uploader's region
    const rift = riftOpen(start, end, regionOfServerName(nameOf(home)));
    if (homeMap !== undefined && Math.floor(home / 1000) === homeMap && !rift) {
      continue;
    }
    const partner = partnerServerAt(home, start);
    if (partner === null) {
      pairKnown = false;
    } else {
      servers.add(partner);
    }
  }
  return { servers, pairKnown };
}

/** The NC server id a servers-table row stands for (null for a label no id can be read from). */
export function ncIdOfServerRow(serverRowId: number | null, names: Map<number, string | null>): number | null {
  const name = serverRowId === null ? null : (names.get(serverRowId) ?? null);
  if (name === null) {
    return null;
  }
  const label = /\[(\d+)\]/.exec(name);
  return label ? Number(label[1]) : (serverOfName(name)?.id ?? null);
}

/**
 * The servers a player can be on given every world boss kill he took part in: what all those fights allow at the same time, narrowed to his faction. Null when he
 * took part in none (nothing is known); an empty set when his fights contradict each other. A fight whose pairing is unknown only counts its home server, and only
 * for the servers of the uploader's faction.
 */
export function plausibleServersOf(playerId: number): Set<number> | null {
  const rows = db.selectDistinct({ id: encounterParticipants.encounterId }).from(encounterParticipants).where(eq(encounterParticipants.playerId, playerId)).all();
  const fights: { servers: Set<number>; pairKnown: boolean; homeFaction: number }[] = [];
  for (const { id } of rows) {
    const allowed = allowedServersOfEncounter(id);
    if (allowed) {
      fights.push({ ...allowed, homeFaction: Math.floor([...allowed.servers][0] / 1000) });
    }
  }
  if (fights.length === 0) {
    return null;
  }
  // a fight whose pairing is unknown allows its home server and, besides, any server of the other faction (it cannot be judged)
  const allows = (f: (typeof fights)[number], s: number) => f.servers.has(s) || (!f.pairKnown && Math.floor(s / 1000) !== f.homeFaction);
  const universe = new Set(fights.flatMap((f) => [...f.servers]));
  let result = [...universe].filter((s) => fights.every((f) => allows(f, s)));
  const faction = db.select({ f: players.faction }).from(players).where(eq(players.id, playerId)).get()?.f;
  if (faction === "Elyos" || faction === "Asmodian") {
    const digit = faction === "Elyos" ? 1 : 2;
    result = result.filter((s) => Math.floor(s / 1000) === digit);
  }
  return new Set(result);
}
