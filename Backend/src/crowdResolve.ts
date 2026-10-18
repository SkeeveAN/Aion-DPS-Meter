import { and, eq, like } from "drizzle-orm";
import { db } from "./db/client.js";
import { uploads } from "./db/schema.js";
import { serverOfName } from "./content/aion2Servers.js";
import { searchHits } from "./ncMatch.js";
import { shardOf } from "./ncMatch.js";
import { ncFetch } from "./portraitService.js";
import { matchedServer } from "./serverMatchmakingDb.js";
import { isCompletePlayer, uploadSchema, type UploadPayload } from "./uploadSchema.js";
import { acceptUpload } from "./routes/uploads.js";

/**
 * A world boss has a thousand players on it, and the client knows server and faction of only a few of them. Nobody is given a server or a faction
 * by a guess (not even the uploader's own as a fallback): the server asks NC's character search, on BOTH servers of the pairing (the Elyos server
 * and the Asmodian server that share the world - rifts bring the other faction in), and a player counts only when NC finds exactly one character
 * of that name there (a second hit is told apart by the class the upload names). Whoever is not found that way stays without server and faction
 * and is left out by the top rule (uploadSchema.ts isCompletePlayer).
 */
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
