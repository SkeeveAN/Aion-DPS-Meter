import { and, eq, like } from "drizzle-orm";
import { db } from "./db/client.js";
import { uploads } from "./db/schema.js";
import { serverOfName } from "./content/aion2Servers.js";
import { searchHits } from "./ncMatch.js";
import { shardOf } from "./ncMatch.js";
import { ncFetch } from "./portraitService.js";
import { matchedServer } from "./serverMatchmakingDb.js";
import { isCompletePlayer, uploadSchema, type UploadPayload } from "./uploadSchema.js";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { acceptUpload } from "./routes/uploads.js";

/**
 * A world boss has a thousand players on it, and the client knows server and faction of only a few of them. Who they are depends on the clock:
 *  - Verteron/Altgard field bosses outside the rift hours (one hour from 00:00, 03:00 ... 21:00 UTC, during which the other faction may cross over;
 *    after the hour everybody is sent home): only home players are on the map, so the crowd is the uploader's server and faction ("Kaisinel = Elyos").
 *  - During the rift hours, and always on the Abyss bosses (permanent pairing): NC's character search is asked, on BOTH servers of the pairing, for every
 *    player the client has no server or faction of. A player counts only when NC finds exactly one character of that name there (a second hit is told
 *    apart by the class the upload names); whoever is not placed stays without server and faction and is left out by the top rule (isCompletePlayer).
 * Where the rule is not known (a world boss that is on no list, an uploader of the other faction than the map) NC is asked, never a guess.
 */
// ---- the clock: who can be on the map (riftHours.ts) --------------------------------------------------------------------------------------------
export { RIFT_EVERY_HOURS, riftOpen } from "./riftHours.js";
import { riftOpen } from "./riftHours.js";

type BossGroup = "verteron" | "altgard" | "abyss";
let groupOf: Map<number, BossGroup> | null = null;

function bossGroup(npcId: number | undefined): BossGroup | null {
  if (groupOf === null) {
    groupOf = new Map();
    try {
      const file = join(dirname(fileURLToPath(import.meta.url)), "data", "aion2", "worldbosses.json");
      for (const item of (JSON.parse(readFileSync(file, "utf8")) as { items: { group: BossGroup; npcId: number }[] }).items) {
        groupOf.set(item.npcId, item.group);
      }
    } catch {
      // no list: every world boss is looked up
    }
  }
  return npcId === undefined ? null : (groupOf.get(npcId) ?? null);
}

/**
 * How the crowd of a world boss kill is placed: "home" (the uploader's server and faction), "lookup" (NC, both servers of the pairing).
 * Home only for a field boss of the uploader's own map with no rift hour open (in every region) - everything else is looked up.
 */
export function crowdMode(payload: UploadPayload): "home" | "lookup" {
  const group = bossGroup(payload.bossNpcId);
  const self = payload.participants.find((p) => p.isSelf);
  const mapFaction = group === "verteron" ? "Elyos" : group === "altgard" ? "Asmodian" : null;
  if (mapFaction === null || self === undefined || self.faction !== mapFaction || self.serverId === undefined) {
    return "lookup";
  }
  return riftOpen(Date.parse(payload.startedAt), Date.parse(payload.endedAt)) ? "lookup" : "home";
}

/** The home rule: players without server or faction are the uploader's (outside the rift hours only home players are on the map). Returns how many were filled. */
export function fillHomeCrowd(payload: UploadPayload): number {
  const self = payload.participants.find((p) => p.isSelf)!;
  let filled = 0;
  for (const p of payload.participants) {
    if (p.isSelf || isCompletePlayer(p)) {
      continue;
    }
    p.faction = self.faction;
    p.serverId = self.serverId;
    filled++;
  }
  return filled;
}

/**
 * The servers a player of this world boss kill can be on (the rule of crowdMode above): the uploader's own outside the rift hours on a home-map field boss, else his
 * and the one he is paired with right now. `pairKnown` is false when no pairing is on record (then the other faction cannot be judged). Null without an uploader's server.
 */
export function allowedServers(payload: UploadPayload): { servers: Set<number>; pairKnown: boolean } | null {
  const self = payload.participants.find((p) => p.isSelf);
  if (self?.serverId === undefined) {
    return null;
  }
  if (crowdMode(payload) === "home") {
    return { servers: new Set([self.serverId]), pairKnown: true };
  }
  const servers = new Set(candidateServers(payload));
  return { servers, pairKnown: [...servers].some((id) => Math.floor(id / 1000) !== Math.floor(self.serverId! / 1000)) };
}

/**
 * A client names a server for players of a crowd it cannot know it of (the uploader's own for everybody, a namesake's, an id nobody has): a server outside the rule is
 * wrong. The player loses server and faction like a player the client knows nothing of, and is placed by the clock or looked up at NC (both servers of the pairing)
 * like him. Returns how many lost their server.
 */
export function dropServersOutsideRule(payload: UploadPayload): number {
  const rule = allowedServers(payload);
  if (rule === null) {
    return 0;
  }
  const home = Math.floor(payload.participants.find((p) => p.isSelf)!.serverId! / 1000);
  let dropped = 0;
  for (const p of payload.participants) {
    if (p.isSelf || p.serverId === undefined || rule.servers.has(p.serverId) || (!rule.pairKnown && Math.floor(p.serverId / 1000) !== home)) {
      continue;
    }
    delete p.serverId;
    p.faction = "";
    dropped++;
  }
  return dropped;
}

export const CROWD_PENDING = "crowd_pending";
/** Marks a player NC could not place uniquely; not Elyos or Asmodian, so the top rule leaves him out. */
export const CROWD_UNRESOLVED = "?";

export type CrowdVerdict = { serverId: number; faction: "Elyos" | "Asmodian" };
type FetchFn = typeof fetch;

const CLASS_OF_ID = ["", "Gladiator", "Templar", "Ranger", "Assassin", "Spiritmaster", "Sorcerer", "Cleric", "Chanter"];

/** Off under tests unless a test sets CROWD_LOOKUP=on (no NC there). */
export const crowdLookupEnabled = () => (process.env.CROWD_LOOKUP ?? (process.env.NODE_TEST_CONTEXT ? "off" : "on")).toLowerCase() !== "off";

/** The players of the upload that still lack server or faction and were not looked up yet. */
export function crowdToLookUp(payload: UploadPayload) {
  return payload.participants.filter((p) => !isCompletePlayer(p) && p.faction !== CROWD_UNRESOLVED);
}

/** The server ids the crowd can be from: the uploader's server and the one it is paired with right now (none when no pairing is known). */
export function candidateServers(payload: UploadPayload): number[] {
  const ids = new Set<number>();
  const self = payload.participants.find((p) => p.isSelf);
  if (self?.serverId !== undefined) {
    ids.add(self.serverId);
  }
  const m = /^(.+?) - (.+)$/.exec(payload.serverName ?? "");
  if (m) {
    const own = serverOfName(payload.serverName);
    if (own) {
      ids.add(own.id);
    }
    const partner = matchedServer(m[1], m[2]);
    const partnerId = partner ? serverOfName(`${m[1]} - ${partner}`) : null;
    if (partnerId) {
      ids.add(partnerId.id);
    }
  }
  return [...ids];
}

/** NC's answer for one name: the single character of that name on the candidate servers, or null (none, or not told apart). Throws when NC cannot be reached. */
export async function resolveName(fetchFn: FetchFn, name: string, className: string, candidates: number[], serverName: string | undefined): Promise<CrowdVerdict | null> {
  const shard = shardOf(serverName);
  if (shard === null || candidates.length === 0) {
    return null;
  }
  const lower = name.toLowerCase();
  // One search per candidate server (NC filters on its side): a common name has far more hits than the pages read ("Pixie": 175), the two servers have one or none.
  const hits = [];
  for (const serverId of candidates) {
    hits.push(...(await searchHits(fetchFn, name, shard, { serverId })).filter((h) => h.serverId === serverId && h.name.toLowerCase() === lower));
  }
  const one = hits.length === 1 ? hits : hits.filter((h) => h.classId !== null && CLASS_OF_ID[h.classId] === className);
  return one.length === 1 ? { serverId: one[0].serverId, faction: one[0].race === 1 ? "Elyos" : "Asmodian" } : null;
}

const cache = new Map<string, { verdict: CrowdVerdict | null; until: number }>();
const CACHE_FOR = 6 * 60 * 60_000;

/** Looks up up to `batch` of the players still open; returns how many are left. Progress is kept in the payload (a player not placed is marked CROWD_UNRESOLVED). */
export async function lookUpCrowd(payload: UploadPayload, batch: number, fetchFn: FetchFn = ncFetch, now = Date.now()): Promise<number> {
  const open = crowdToLookUp(payload);
  const candidates = candidateServers(payload);
  for (const p of open.slice(0, batch)) {
    const key = `${p.name.toLowerCase()}|${p.className}|${candidates.join(",")}`;
    let hit = cache.get(key);
    if (!hit || hit.until < now) {
      hit = { verdict: await resolveName(fetchFn, p.name, p.className, candidates, payload.serverName), until: now + CACHE_FOR };
      cache.set(key, hit);
    }
    if (hit.verdict) {
      p.serverId = hit.verdict.serverId;
      p.faction = hit.verdict.faction;
    } else {
      p.faction = CROWD_UNRESOLVED;
    }
  }
  return Math.max(0, open.length - batch);
}

let running = false;

/** One round: works on the oldest upload that waits for its crowd and files it when every player is looked up. Returns true when it did something. */
export async function settleCrowdPending(log: Parameters<typeof acceptUpload>[0], batch = 25, fetchFn: FetchFn = ncFetch): Promise<boolean> {
  if (running) {
    return false;
  }
  running = true;
  try {
    const row = db.select().from(uploads).where(and(eq(uploads.status, "pending"), like(uploads.reviewReason, `%${CROWD_PENDING}%`))).orderBy(uploads.id).limit(1).get();
    if (!row) {
      return false;
    }
    const parsed = uploadSchema.safeParse(JSON.parse(row.rawPayloadJson));
    if (!parsed.success) {
      db.update(uploads).set({ status: "rejected", reviewedAt: new Date().toISOString() }).where(eq(uploads.id, row.id)).run();
      return true;
    }
    const payload = parsed.data;
    let left: number;
    try {
      left = await lookUpCrowd(payload, batch, fetchFn);
    } catch (err) {
      log.warn({ err: String(err), uploadId: row.id }, "crowd lookup: NC not reachable, tries again");
      db.update(uploads).set({ rawPayloadJson: JSON.stringify(payload) }).where(eq(uploads.id, row.id)).run();
      return false;
    }
    if (left > 0) {
      db.update(uploads).set({ rawPayloadJson: JSON.stringify(payload) }).where(eq(uploads.id, row.id)).run();
      return true;
    }
    const placed = payload.participants.filter((p) => isCompletePlayer(p)).length;
    log.info({ uploadId: row.id, bossNpcName: payload.bossNpcName, players: payload.participants.length, placed }, "crowd lookup done, the upload is filed");
    const out = await acceptUpload(log, payload, row.ipHash, true);
    // acceptUpload wrote its own row (merged, turned away or waiting); the waiting one is replaced by it
    db.delete(uploads).where(eq(uploads.id, row.id)).run();
    log.info({ uploadId: row.id, status: out.status }, "crowd upload settled");
    return true;
  } finally {
    running = false;
  }
}

export function startCrowdSweep(log: Parameters<typeof acceptUpload>[0], intervalMs = 20_000): void {
  if (!crowdLookupEnabled() || process.env.NODE_TEST_CONTEXT) {
    return;
  }
  setInterval(() => {
    settleCrowdPending(log).catch((err) => log.error({ err: String(err) }, "crowd sweep failed"));
  }, intervalMs).unref();
}
