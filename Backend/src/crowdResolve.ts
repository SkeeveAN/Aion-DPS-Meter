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
 *  - Verteron/Altgard field bosses outside the rift hours (one hour from 02:00, 05:00, 08:00 ... 23:00 in Europe, during which the other faction may cross over;
 *    after the hour everybody is sent home): only home players are on the map, so the crowd is the uploader's server and faction ("Kaisinel = Elyos").
 *  - During the rift hours, and always on the Abyss bosses (permanent pairing): NC's character search is asked, on BOTH servers of the pairing, for every
 *    player the client has no server or faction of. A player counts only when NC finds exactly one character of that name there (a second hit is told
 *    apart by the class the upload names); whoever is not placed stays without server and faction and is left out by the top rule (isCompletePlayer).
 * Where the rule is not known (another region, a world boss that is on no list) NC is asked, never a guess.
 */
// ---- the clock: who can be on the map ------------------------------------------------------------------------------------------------------------

/** The rift hours: a spacetime rift opens every three hours (02:00, 05:00 ... 23:00, as in the timetable) and the other faction can cross over for the hour after it (Europe, local time); everybody is sent home when the hour ends. */
export const RIFT_HOURS = [2, 5, 8, 11, 14, 17, 20, 23];
/** A little slack on both sides: the game's clock and the client's are not the same, and a fight can reach over the edge. */
const RIFT_SLACK_MS = 2 * 60_000;
const RIFT_ZONE = "Europe/Berlin";

const minuteOfHour = new Intl.DateTimeFormat("en-GB", { timeZone: RIFT_ZONE, hour: "numeric", minute: "numeric", hourCycle: "h23" });

function localMinutes(at: number): number {
  const parts = minuteOfHour.formatToParts(new Date(at));
  return Number(parts.find((p) => p.type === "hour")?.value) * 60 + Number(parts.find((p) => p.type === "minute")?.value);
}

/** Whether a rift hour is open (or just about) at the moment of the kill. */
export function riftOpen(startedAt: number, endedAt: number): boolean {
  for (const at of [startedAt - RIFT_SLACK_MS, startedAt, endedAt, endedAt + RIFT_SLACK_MS]) {
    const m = localMinutes(at);
    if (RIFT_HOURS.some((h) => m >= h * 60 && m < (h + 1) * 60)) {
      return true;
    }
  }
  return false;
}

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
 * Home only for a field boss of the uploader's own map, in Europe, with no rift hour open - everything else is looked up.
 */
export function crowdMode(payload: UploadPayload): "home" | "lookup" {
  const group = bossGroup(payload.bossNpcId);
  const self = payload.participants.find((p) => p.isSelf);
  const mapFaction = group === "verteron" ? "Elyos" : group === "altgard" ? "Asmodian" : null;
  if (mapFaction === null || self === undefined || self.faction !== mapFaction || self.serverId === undefined || !/^Europe - /.test(payload.serverName ?? "")) {
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
  const hits = (await searchHits(fetchFn, name, shard)).filter((h) => h.name.toLowerCase() === lower && candidates.includes(h.serverId));
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
