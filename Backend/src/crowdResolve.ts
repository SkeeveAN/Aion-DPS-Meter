import { and, eq, like } from "drizzle-orm";
import { db } from "./db/client.js";
import { uploads } from "./db/schema.js";
import { serverOfName } from "./content/aion2Servers.js";
import { searchHits } from "./ncMatch.js";
import { shardOf } from "./ncMatch.js";
import { ncFetch, portraitQueue } from "./portraitService.js";
import type { MatchSource } from "./portraitService.js";
import { abyssMatchedServer, matchedServer } from "./serverMatchmakingDb.js";
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
export { RIFT_EVERY_HOURS, riftOpen, regionOfServerName } from "./riftHours.js";
import { regionOfServerName, riftOpen } from "./riftHours.js";

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
  return riftOpen(Date.parse(payload.startedAt), Date.parse(payload.endedAt), regionOfServerName(payload.serverName)) ? "lookup" : "home";
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
    // The Abyss has pairs of its own (since 2026-10-28, often two servers of one faction); the field bosses of the home maps follow the rift pairing.
    const partner = (bossGroup(payload.bossNpcId) === "abyss" ? abyssMatchedServer(m[1], m[2]) : null) ?? matchedServer(m[1], m[2]);
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

/**
 * A player whose faction the client knows is placed by the pairing without asking NC: of the servers the crowd can be from (his own and the one it is paired with) exactly
 * one has his faction - the rift pairs join an Elyos and an Asmodian server, so an Elyos of a Kaisinel kill is on Kaisinel and an Asmodian on Israphel. Where both servers
 * have his faction (an Abyss pair of one faction) or none does, NC is asked as before. Returns how many were placed.
 */
export function placeByFaction(payload: UploadPayload): number {
  const candidates = candidateServers(payload);
  let placed = 0;
  for (const p of crowdToLookUp(payload)) {
    if (p.faction !== "Elyos" && p.faction !== "Asmodian") {
      continue;
    }
    const side = p.faction === "Elyos" ? 1 : 2;
    const fitting = candidates.filter((id) => Math.floor(id / 1000) === side);
    if (fitting.length === 1) {
      p.serverId = fitting[0];
      placed++;
    }
  }
  return placed;
}

/** One name at NC (cached for six hours): marks the player placed or CROWD_UNRESOLVED. Throws when NC cannot be reached. */
async function lookUpPlayer(p: UploadPayload["participants"][number], payload: UploadPayload, candidates: number[], fetchFn: FetchFn, now: number): Promise<void> {
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

/** Looks up up to `batch` of the players still open; returns how many are left. Progress is kept in the payload (a player not placed is marked CROWD_UNRESOLVED). */
export async function lookUpCrowd(payload: UploadPayload, batch: number, fetchFn: FetchFn = ncFetch, now = Date.now()): Promise<number> {
  placeByFaction(payload);
  const open = crowdToLookUp(payload);
  const candidates = candidateServers(payload);
  for (const p of open.slice(0, batch)) {
    await lookUpPlayer(p, payload, candidates, fetchFn, now);
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

type Log = Parameters<typeof acceptUpload>[0];

/**
 * The crowd lookups as work for the workers of the PortraitQueue (the same four workers, the same learned pace as everything else asked of NC): one unit is one
 * player of the oldest upload that waits for its crowd. The upload is held in memory while its players are worked off by all workers at once, saved every 25 players, and
 * filed when the last one is placed. After a restart it is read from the database again (what was saved stays placed).
 */
export class CrowdSource implements MatchSource {
  host: { wake(): void } | null = null;
  private current: { rowId: number; ipHash: string; payload: UploadPayload; candidates: number[]; reserved: Set<number>; inFlight: number; sinceSave: number; closing: boolean } | null = null;
  private pending = 0;
  private cooldownUntil = 0;

  constructor(
    private readonly log: Log,
    private readonly fetchFn: FetchFn = ncFetch,
    private readonly now: () => number = Date.now,
  ) {}

  private openIndexes(): number[] {
    const cur = this.current;
    if (!cur) {
      return [];
    }
    const open: number[] = [];
    cur.payload.participants.forEach((p, i) => {
      if (!cur.reserved.has(i) && !isCompletePlayer(p) && p.faction !== CROWD_UNRESOLVED) {
        open.push(i);
      }
    });
    return open;
  }

  /** Players that can be taken right now (a waiting upload that is not loaded yet counts as one). */
  get size(): number {
    if (this.now() < this.cooldownUntil) {
      return 0;
    }
    return this.current ? (this.current.closing ? 0 : this.openIndexes().length) : this.pending;
  }

  next(): { background: boolean } | null {
    return this.size > 0 ? { background: false } : null;
  }

  /** Counts the uploads that wait and wakes the workers: called by a timer, and when an upload has been filed. */
  refresh(): void {
    this.pending = db.select({ id: uploads.id }).from(uploads).where(and(eq(uploads.status, "pending"), like(uploads.reviewReason, `%${CROWD_PENDING}%`))).all().length;
    if (this.size > 0) {
      this.host?.wake();
    }
    if (this.pending > 0) {
      this.log.info({ pending: this.pending, loaded: this.current?.rowId ?? null, size: this.size, cooldownMs: Math.max(0, this.cooldownUntil - this.now()), workers: portraitQueue.state() }, "crowd lookups waiting");
    }
  }

  /** Loads the oldest waiting upload; false when there is none or it needs nothing from NC any more (it is then filed). */
  private load(): boolean {
    for (;;) {
      const row = db.select().from(uploads).where(and(eq(uploads.status, "pending"), like(uploads.reviewReason, `%${CROWD_PENDING}%`))).orderBy(uploads.id).limit(1).get();
      if (!row) {
        this.pending = 0;
        return false;
      }
      const parsed = uploadSchema.safeParse(JSON.parse(row.rawPayloadJson));
      if (!parsed.success) {
        db.update(uploads).set({ status: "rejected", reviewedAt: new Date().toISOString() }).where(eq(uploads.id, row.id)).run();
        continue;
      }
      const payload = parsed.data;
      placeByFaction(payload);
      this.current = { rowId: row.id, ipHash: row.ipHash, payload, candidates: candidateServers(payload), reserved: new Set(), inFlight: 0, sinceSave: 1, closing: false };
      if (this.openIndexes().length === 0) {
        void this.finish(this.current);
        return false;
      }
      return true;
    }
  }

  take(): number | undefined {
    if (this.now() < this.cooldownUntil || (!this.current && !this.load())) {
      return undefined;
    }
    const cur = this.current;
    const index = cur && !cur.closing ? this.openIndexes()[0] : undefined;
    if (cur === null || index === undefined) {
      return undefined;
    }
    cur.reserved.add(index);
    cur.inFlight++;
    return index;
  }

  async run(index: number): Promise<void> {
    const cur = this.current;
    if (!cur) {
      return;
    }
    try {
      await lookUpPlayer(cur.payload.participants[index], cur.payload, cur.candidates, this.fetchFn, this.now());
    } catch (err) {
      // NC could not be reached: the player stays open, nothing is asked for half a minute
      this.log.warn({ err: String(err), uploadId: cur.rowId }, "crowd lookup: NC not reachable, tries again");
      this.cooldownUntil = this.now() + 30_000;
    }
    cur.reserved.delete(index);
    cur.inFlight--;
    cur.sinceSave++;
    if (cur.sinceSave >= 25) {
      this.save(cur);
    }
    if (cur.inFlight === 0 && !cur.closing && this.openIndexes().length === 0 && crowdToLookUp(cur.payload).length === 0) {
      await this.finish(cur);
    }
  }

  private save(cur: NonNullable<CrowdSource["current"]>): void {
    cur.sinceSave = 0;
    db.update(uploads).set({ rawPayloadJson: JSON.stringify(cur.payload) }).where(eq(uploads.id, cur.rowId)).run();
  }

  private async finish(cur: NonNullable<CrowdSource["current"]>): Promise<void> {
    cur.closing = true;
    try {
      const placed = cur.payload.participants.filter((p) => isCompletePlayer(p)).length;
      this.log.info({ uploadId: cur.rowId, bossNpcName: cur.payload.bossNpcName, players: cur.payload.participants.length, placed }, "crowd lookup done, the upload is filed");
      const out = await acceptUpload(this.log, cur.payload, cur.ipHash, true);
      // acceptUpload wrote its own row (merged, turned away or waiting); the waiting one is replaced by it
      db.delete(uploads).where(eq(uploads.id, cur.rowId)).run();
      this.log.info({ uploadId: cur.rowId, status: out.status }, "crowd upload settled");
    } catch (err) {
      this.log.error({ err: String(err), uploadId: cur.rowId }, "crowd upload could not be filed, tries again");
      this.save(cur);
      this.cooldownUntil = this.now() + 60_000;
    }
    this.current = null;
    this.refresh();
  }
}

export const crowdSource = { instance: null as CrowdSource | null };

export function startCrowdSweep(log: Log, intervalMs = 20_000): void {
  if (!crowdLookupEnabled() || process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const source = new CrowdSource(log);
  crowdSource.instance = source;
  portraitQueue.attachCrowd(source);
  setInterval(() => source.refresh(), intervalMs).unref();
}
