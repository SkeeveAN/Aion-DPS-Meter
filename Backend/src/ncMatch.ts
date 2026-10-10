import { and, eq, inArray, ne, sql } from "drizzle-orm";
import { db } from "./db/client.js";
import { encounterParticipants, playerNcMatch, playerProfiles, players, servers } from "./db/schema.js";
import { AION2_CLASS_BY_ID } from "./constants.js";
import { canonicalServer } from "./content/aion2Servers.js";
import { linkPlayerGuild } from "./guilds.js";
import { MAX_SEARCH_BYTES, SEARCH_URL, getNc, ncFetch } from "./portraitService.js";

/**
 * Checks players against NC's public character search, so that the server and the faction of every player we have seen are the real ones.
 *
 * Old clients filed every player of an upload under the uploader's server, and most players are only seen in a group: nothing in the upload
 * says where they play. NC's search knows: it lists every character of that name with its server, race, class and level. A name alone is
 * not enough (the same name exists on many servers), so the candidates are narrowed by what we know about the player - faction, class,
 * level, and the verified members of his legion (a legion belongs to one server). Only ONE remaining character is applied; with
 * several nothing is guessed and the check is repeated later, when newer uploads know more.
 *
 * Be polite: one request at a time through the shared paced fetch (portraitService.ncFetch), a bounded queue, results are remembered and
 * only players that are due are asked again.
 */

type FetchFn = typeof fetch;

const HOUR = 3_600_000;
const DAY = 24 * HOUR;
/** When to ask again, by outcome. A verified player is looked at again now and then (a transfer or a rename); an unclear one when newer uploads may know more. */
export const MATCH_RETRY_AFTER = { verified: 30 * DAY, client: 365 * DAY, ambiguous: 3 * DAY, none: 2 * DAY, conflict: 7 * DAY, error: 1 * HOUR } as const;
export type MatchStatus = keyof typeof MATCH_RETRY_AFTER;

const SHARDS: Record<string, string> = { Europe: "eu", Asia: "as", "NA East": "nae", "NA West": "naw", LATAM: "la" };
/** NC's region key of a server label ("Europe - Kaisinel" -> "eu"); null when the label has no region. */
export function shardOf(displayName: string | null | undefined): string | null {
  return SHARDS[(displayName ?? "").split(" - ")[0]] ?? null;
}

export type Hit = { name: string; race: 1 | 2; serverId: number; level: number; classId: number | null; characterId: string | null };

const stripTags = (s: string) => s.replace(/<[^>]*>/g, "");

/** The characters of a search answer's `list` (untrusted data): name, race, server, level and class id ((pcId - 1) / 4). */
export function parseHits(list: unknown): Hit[] {
  if (!Array.isArray(list)) {
    return [];
  }
  const hits: Hit[] = [];
  for (const e of list as Record<string, unknown>[]) {
    const serverId = Number(e?.serverId);
    const race = Number(e?.race);
    if (typeof e?.name !== "string" || (race !== 1 && race !== 2) || !Number.isInteger(serverId) || serverId < 1000 || serverId > 9999) {
      continue;
    }
    const pc = Number(e.pcId);
    hits.push({
      name: stripTags(e.name),
      race,
      serverId,
      level: Number.isFinite(Number(e.level)) ? Number(e.level) : 0,
      classId: Number.isInteger(pc) && pc >= 1 ? Math.floor((pc - 1) / 4) : null,
      characterId: typeof e.characterId === "string" ? e.characterId : null,
    });
  }
  return hits;
}

/** What we know about a player that helps to tell characters of the same name apart. */
export type Subject = {
  name: string;
  /** "Elyos" | "Asmodian" | "" */
  faction: string;
  /** English class name, "" when unknown. */
  className: string;
  /** True when the class was read off his skills in a fight (reliable), false when it only comes from his profile. */
  classFromFight: boolean;
  /** Level from his profile; a character never loses levels, so the hit's level must not be lower. */
  level: number | null;
  /** The server his legion is on, when two or more verified members of it agree; empty otherwise. */
  legionServers: number[];
};

export type Decision = { status: "verified" | "ambiguous" | "none"; hit: Hit | null; via: "name" | "legion" | null; candidates: number };

const RACE_OF = { Elyos: 1, Asmodian: 2 } as const;

/** Picks the one character a name search is about, or says why not. Never guesses. */
export function decide(hits: Hit[], s: Subject): Decision {
  let c = hits.filter((h) => h.name.toLowerCase() === s.name.toLowerCase());
  if (c.length === 0) {
    return { status: "none", hit: null, via: null, candidates: 0 };
  }
  if (s.faction === "Elyos" || s.faction === "Asmodian") {
    c = c.filter((h) => h.race === RACE_OF[s.faction as "Elyos" | "Asmodian"]);
    if (c.length === 0) {
      return { status: "none", hit: null, via: null, candidates: 0 };
    }
  }
  if (c.length > 1 && s.className) {
    const same = c.filter((h) => h.classId !== null && AION2_CLASS_BY_ID[h.classId] === s.className);
    if (same.length > 0 && c.every((h) => h.classId !== null)) {
      c = same;
    }
  }
  if (c.length > 1 && s.level) {
    const reached = c.filter((h) => h.level >= (s.level ?? 0));
    if (reached.length > 0) {
      c = reached;
    }
  }
  if (c.length === 1) {
    // A lone character of another class is somebody else with the same name (our player was renamed or deleted).
    if (s.classFromFight && s.className && c[0].classId !== null && AION2_CLASS_BY_ID[c[0].classId] !== s.className) {
      return { status: "none", hit: null, via: null, candidates: 1 };
    }
    return { status: "verified", hit: c[0], via: "name", candidates: 1 };
  }
  if (s.legionServers.length === 1) {
    const there = c.filter((h) => h.serverId === s.legionServers[0]);
    if (there.length === 1) {
      return { status: "verified", hit: there[0], via: "legion", candidates: c.length };
    }
  }
  return { status: "ambiguous", hit: null, via: null, candidates: c.length };
}

/** Every character of that name NC lists in a region (at most three pages of 40; the search is a prefix/substring search, so exact names are picked later). */
export async function searchHits(fetchFn: FetchFn, name: string, shard: string): Promise<Hit[]> {
  const all: Hit[] = [];
  for (let page = 1; page <= 3; page++) {
    const url = new URL(SEARCH_URL);
    url.searchParams.set("keyword", name);
    url.searchParams.set("region", shard);
    url.searchParams.set("localeInfo", "en-US");
    url.searchParams.set("page", String(page));
    url.searchParams.set("size", "40");
    const { body } = await getNc(fetchFn, url, MAX_SEARCH_BYTES);
    const answer = JSON.parse(body.toString("utf8")) as { list?: unknown; pagination?: { endPage?: number } };
    all.push(...parseHits(answer.list));
    if (page >= Number(answer.pagination?.endPage ?? 1)) {
      break;
    }
  }
  return all;
}

type PlayerRow = { id: number; name: string; nameNormalized: string; serverId: number | null; serverName: string | null; faction: string; guildId: number | null };

function loadPlayer(playerId: number): PlayerRow | undefined {
  return db
    .select({ id: players.id, name: players.name, nameNormalized: players.nameNormalized, serverId: players.serverId, serverName: servers.displayName, faction: players.faction, guildId: players.guildId })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where(eq(players.id, playerId))
    .get();
}

/** The server ids two or more verified members of the player's legion agree on (empty when fewer, or when they disagree). */
function legionServersOf(player: PlayerRow): number[] {
  if (player.guildId === null) {
    return [];
  }
  const rows = db
    .select({ ncServerId: playerNcMatch.ncServerId })
    .from(players)
    .innerJoin(playerNcMatch, eq(playerNcMatch.playerId, players.id))
    .where(and(eq(players.guildId, player.guildId), ne(players.id, player.id), inArray(playerNcMatch.status, ["verified", "client"])))
    .all()
    .map((r) => r.ncServerId)
    .filter((x): x is number => x !== null);
  const distinct = new Set(rows);
  return rows.length >= 2 && distinct.size === 1 ? [...distinct] : [];
}

function subjectOf(player: PlayerRow): Subject {
  const fight = db
    .select({ className: encounterParticipants.className })
    .from(encounterParticipants)
    .where(and(eq(encounterParticipants.playerId, player.id), ne(encounterParticipants.className, ""), ne(encounterParticipants.className, "?")))
    .orderBy(sql`${encounterParticipants.id} desc`)
    .get();
  const profile = db.select({ classId: playerProfiles.classId, level: playerProfiles.level }).from(playerProfiles).where(eq(playerProfiles.playerId, player.id)).get();
  const profileClass = profile?.classId ? AION2_CLASS_BY_ID[profile.classId] : undefined;
  return {
    name: player.name,
    faction: player.faction,
    className: fight?.className ?? profileClass ?? "",
    classFromFight: !!fight,
    level: profile?.level ?? null,
    legionServers: legionServersOf(player),
  };
}

/** The row of a server by its NC id, created when it is new (same naming as the upload path: "Europe - Kaisinel"). */
function serverRowOf(ncServerId: number): number {
  const { fingerprint, displayName } = canonicalServer(`aion2:aion-2-server-${ncServerId}`, undefined);
  const found = db.select({ id: servers.id }).from(servers).where(and(eq(servers.fingerprint, fingerprint), eq(servers.displayName, displayName ?? ""))).get();
  if (found) {
    return found.id;
  }
  return Number(db.insert(servers).values({ fingerprint, displayName }).run().lastInsertRowid);
}

function record(playerId: number, status: MatchStatus, extra: { ncServerId?: number | null; ncRace?: number | null; via?: string | null; candidates?: number }, now: number) {
  const values = {
    status,
    ncServerId: extra.ncServerId ?? null,
    ncRace: extra.ncRace ?? null,
    via: extra.via ?? null,
    candidates: extra.candidates ?? 0,
    checkedAt: now,
    nextTryAt: now + MATCH_RETRY_AFTER[status],
  };
  db.insert(playerNcMatch).values({ playerId, ...values }).onConflictDoUpdate({ target: playerNcMatch.playerId, set: values }).run();
}

/** Applies the one matching character to the player: his real server, his faction. Returns false (nothing changed) when it contradicts what we hold. */
function applyHit(player: PlayerRow, hit: Hit): "applied" | "conflict" {
  const faction = hit.race === 1 ? "Elyos" : "Asmodian";
  if (player.faction && player.faction !== faction) {
    return "conflict";
  }
  const target = serverRowOf(hit.serverId);
  if (target !== player.serverId) {
    const taken = db.select({ id: players.id }).from(players).where(and(eq(players.serverId, target), eq(players.nameNormalized, player.nameNormalized), ne(players.id, player.id))).get();
    if (taken) {
      return "conflict";
    }
    // The legion belongs to one server: when two or more verified members of it say another one, this match is suspect.
    const others = legionServersOf(player);
    if (others.length === 1 && others[0] !== hit.serverId) {
      return "conflict";
    }
    db.update(players).set({ serverId: target, faction }).where(eq(players.id, player.id)).run();
    linkPlayerGuild(player.id);
  } else if (!player.faction) {
    db.update(players).set({ faction }).where(eq(players.id, player.id)).run();
  }
  return "applied";
}

/**
 * Checks one player now: searches his name in the region of his server, decides, applies a single match and records the outcome.
 * Never throws; a network problem becomes status 'error' and is tried again in an hour. Fetch and clock are injectable for tests.
 */
export async function processMatch(playerId: number, fetchFn: FetchFn = ncFetch, now = Date.now()): Promise<MatchStatus | null> {
  const player = loadPlayer(playerId);
  const shard = shardOf(player?.serverName);
  if (!player || !shard) {
    return null;
  }
  let hits: Hit[];
  try {
    hits = await searchHits(fetchFn, player.name, shard);
  } catch {
    record(playerId, "error", {}, now);
    return "error";
  }
  const decision = decide(hits, subjectOf(player));
  if (decision.status === "verified" && decision.hit) {
    const outcome = applyHit(player, decision.hit);
    if (outcome === "conflict") {
      record(playerId, "conflict", { ncServerId: decision.hit.serverId, ncRace: decision.hit.race, candidates: decision.candidates }, now);
      return "conflict";
    }
    record(playerId, "verified", { ncServerId: decision.hit.serverId, ncRace: decision.hit.race, via: decision.via, candidates: decision.candidates }, now);
    return "verified";
  }
  record(playerId, decision.status, { candidates: decision.candidates }, now);
  return decision.status;
}

/** A server the player's own client named: as good as a match, nothing to ask NC. */
export function noteClientServer(playerId: number, ncServerId: number, now = Date.now()): void {
  const before = db.select().from(playerNcMatch).where(eq(playerNcMatch.playerId, playerId)).get();
  if (before?.status === "verified" && before.ncServerId === ncServerId) {
    return;
  }
  record(playerId, "client", { ncServerId }, now);
}

/** Of these players, the ones with no fresh result and a region NC can search. */
export function dueOf(ids: number[], now = Date.now()): number[] {
  if (ids.length === 0) {
    return [];
  }
  const rows = db
    .select({ id: players.id, serverName: servers.displayName, nextTryAt: playerNcMatch.nextTryAt })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .leftJoin(playerNcMatch, eq(playerNcMatch.playerId, players.id))
    .where(inArray(players.id, ids))
    .all();
  return rows.filter((r) => shardOf(r.serverName) !== null && (r.nextTryAt === null || r.nextTryAt <= now)).map((r) => r.id);
}

/** Serial work queue: one player at a time, bounded, no duplicates. The pause between requests is the shared paced fetch. */
export class MatchQueue {
  private readonly waiting = new Set<number>();
  private running = false;
  constructor(
    private readonly work: (playerId: number) => Promise<unknown>,
    private readonly limit = 2000,
  ) {}

  get size(): number {
    return this.waiting.size;
  }

  enqueue(ids: number[]): number {
    let added = 0;
    for (const id of ids) {
      if (this.waiting.size >= this.limit) {
        break;
      }
      if (!this.waiting.has(id)) {
        this.waiting.add(id);
        added++;
      }
    }
    if (added > 0) {
      void this.drain();
    }
    return added;
  }

  /** Resolves when the queue is empty (tests). */
  async idle(): Promise<void> {
    while (this.running || this.waiting.size > 0) {
      await new Promise((r) => setTimeout(r, 5));
    }
  }

  private async drain(): Promise<void> {
    if (this.running) {
      return;
    }
    this.running = true;
    try {
      while (this.waiting.size > 0) {
        const id = this.waiting.values().next().value as number;
        this.waiting.delete(id);
        try {
          await this.work(id);
        } catch {
          // a failed check is recorded by the worker; one bad player must not stop the queue
        }
      }
    } finally {
      this.running = false;
    }
  }
}

/** Off in tests (node --test sets NODE_TEST_CONTEXT) and with NC_MATCH_OFF=1. */
export const matchEnabled = () => process.env.NC_MATCH_OFF !== "1" && !process.env.NODE_TEST_CONTEXT;

export const matchQueue = new MatchQueue((id) => processMatch(id));

/** After an upload: asks NC about the players of it that are due. Never blocks and never throws. */
export function checkPlayers(ids: number[], queue: MatchQueue = matchQueue, now = Date.now()): number {
  if (!matchEnabled() && queue === matchQueue) {
    return 0;
  }
  try {
    return queue.enqueue(dueOf(ids, now));
  } catch {
    return 0;
  }
}

/** The players that were never checked or whose result ran out, newest first. */
export function duePlayers(limit: number, now = Date.now()): number[] {
  const rows = db
    .select({ id: players.id, serverName: servers.displayName, nextTryAt: playerNcMatch.nextTryAt })
    .from(players)
    .innerJoin(servers, eq(players.serverId, servers.id))
    .leftJoin(playerNcMatch, eq(playerNcMatch.playerId, players.id))
    .where(sql`${servers.fingerprint} like 'aion2:%' and (${playerNcMatch.nextTryAt} is null or ${playerNcMatch.nextTryAt} <= ${now})`)
    .orderBy(sql`${playerNcMatch.nextTryAt} is not null`, sql`${players.lastSeenAt} desc`)
    .all();
  return rows.filter((r) => shardOf(r.serverName) !== null).slice(0, limit).map((r) => r.id);
}

/** Background sweep: a few players that are due, every quarter hour, so players seen before this existed and unclear ones get their turn. */
export function startMatchSweep(intervalMs = 15 * 60_000, batch = 60): void {
  if (!matchEnabled()) {
    return;
  }
  const sweep = () => {
    try {
      matchQueue.enqueue(duePlayers(batch));
    } catch {
      // try again at the next round
    }
  };
  setTimeout(sweep, 60_000).unref();
  setInterval(sweep, intervalMs).unref();
}
