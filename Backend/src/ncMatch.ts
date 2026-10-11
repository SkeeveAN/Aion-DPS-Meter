import { and, eq, inArray, ne, sql } from "drizzle-orm";
import { db } from "./db/client.js";
import { encounterParticipants, playerNcMatch, playerProfiles, players, servers } from "./db/schema.js";
import { AION2_CLASS_BY_ID } from "./constants.js";
import { canonicalServer } from "./content/aion2Servers.js";
import { linkPlayerGuild } from "./guilds.js";
import { normalizeName } from "./matching/roster.js";
import { PortraitQueue, decodeCharacterId, dropNcData, fetchCharacterProfile, MAX_SEARCH_BYTES, SEARCH_URL, getNc, ncFetch, portraitQueue, type PlayerKey } from "./portraitService.js";
import { playerPortraits, playerNcCharacter } from "./db/schema.js";
import { EVENT_STALE_AFTER, PRIORITY, type Priority } from "./refreshPolicy.js";

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
/** When to ask again, by outcome ('client': after NC could not confirm him either; a fresh client player is due at once, but last in line). A verified player is looked at again now and then (a transfer or a rename); an unclear one when newer uploads may know more. */
export const MATCH_RETRY_AFTER = { verified: 30 * DAY, client: 30 * DAY, ambiguous: 3 * DAY, none: 2 * DAY, conflict: 7 * DAY, error: 1 * HOUR } as const;
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
  /** His legion's name as the game reported it, "" when none. */
  guild?: string;
};

export type Decision = { status: "verified" | "ambiguous" | "none"; hit: Hit | null; via: "name" | "legion" | "legion-name" | null; candidates: number; /** The characters still in question when ambiguous. */ remaining?: Hit[] };

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
  return { status: "ambiguous", hit: null, via: null, candidates: c.length, remaining: c };
}

/** Most candidates asked for their legion (one request each): above that the name is too common to settle this way. */
const MAX_LEGION_CANDIDATES = 8;

/**
 * Settles an ambiguous name by legion: NC's character page names each candidate's legion, the game told us his. Exactly one candidate in a
 * legion of that name (and not below his level) is him; none or several leave it open. Network problems throw.
 */
export async function decideByLegion(fetchFn: FetchFn, remaining: Hit[], subject: Subject, shard: string): Promise<Hit | null> {
  const wanted = (subject.guild ?? "").trim().toLowerCase();
  if (!wanted || remaining.length === 0 || remaining.length > MAX_LEGION_CANDIDATES) {
    return null;
  }
  const fits: Hit[] = [];
  for (const h of remaining) {
    const characterId = decodeCharacterId(h.characterId);
    if (!characterId) {
      continue;
    }
    const profile = await fetchCharacterProfile(fetchFn, { characterId, serverId: h.serverId }, shard);
    if (profile && profile.guild.trim().toLowerCase() === wanted && (!subject.level || profile.level >= subject.level)) {
      fits.push(h);
    }
  }
  return fits.length === 1 ? fits[0] : null;
}

/** Every character of that name NC lists in a region, optionally of one race or on one server (at most three pages of 40; the search is a prefix/substring search, so exact names are picked later). */
export async function searchHits(fetchFn: FetchFn, name: string, shard: string, filter: { race?: 1 | 2; serverId?: number } = {}): Promise<Hit[]> {
  const all: Hit[] = [];
  for (let page = 1; page <= 3; page++) {
    const url = new URL(SEARCH_URL);
    url.searchParams.set("keyword", name);
    url.searchParams.set("region", shard);
    // NC filters on its side (a common name is a substring of many others: "Pixie" has 136 hits, 77 of them Elyos).
    if (filter.race) {
      url.searchParams.set("race", String(filter.race));
    }
    if (filter.serverId) {
      url.searchParams.set("serverId", String(filter.serverId));
    }
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

type PlayerRow = { id: number; name: string; nameNormalized: string; serverId: number | null; serverName: string | null; faction: string; guildId: number | null; guild: string | null };

function loadPlayer(playerId: number): PlayerRow | undefined {
  return db
    .select({ id: players.id, name: players.name, nameNormalized: players.nameNormalized, serverId: players.serverId, serverName: servers.displayName, faction: players.faction, guildId: players.guildId, guild: players.guild })
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
    guild: player.guild ?? "",
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

function record(playerId: number, status: MatchStatus, extra: { ncServerId?: number | null; ncRace?: number | null; ncCharacterId?: string | null; via?: string | null; candidates?: number; nextTryAt?: number }, now: number) {
  const values = {
    status,
    ncServerId: extra.ncServerId ?? null,
    ncRace: extra.ncRace ?? null,
    ncCharacterId: extra.ncCharacterId ?? null,
    via: extra.via ?? null,
    candidates: extra.candidates ?? 0,
    checkedAt: now,
    nextTryAt: extra.nextTryAt ?? now + MATCH_RETRY_AFTER[status],
  };
  db.insert(playerNcMatch).values({ playerId, ...values }).onConflictDoUpdate({ target: playerNcMatch.playerId, set: values }).run();
}

/**
 * NC's current name of a confirmed character. The same name (case aside from the stored spelling) changes nothing but the spelling.
 * A new name renames the player in place - slug and history stay, the old name becomes an alias so old uploads still find him - unless
 * another player of that server already carries the new name (his new fights were filed under a second row): then nothing is merged
 * and 'rename-conflict' is returned, for a human. Returns the marker for `via`, or null.
 */
function applyName(player: PlayerRow, ncName: string, serverId: number): string | null {
  if (ncName === player.name) {
    return null;
  }
  const target = normalizeName(ncName);
  if (target === player.nameNormalized) {
    db.update(players).set({ name: ncName }).where(eq(players.id, player.id)).run();
    return null;
  }
  const taken = db.select({ id: players.id }).from(players).where(and(eq(players.serverId, serverRowOf(serverId)), eq(players.nameNormalized, target), ne(players.id, player.id))).get();
  if (taken) {
    return "rename-conflict";
  }
  const row = db.select({ aliases: players.aliasNamesNormalized }).from(players).where(eq(players.id, player.id)).get();
  const aliases = [...new Set([...(row?.aliases ?? []), player.nameNormalized])];
  db.update(players).set({ name: ncName, nameNormalized: target, aliasNamesNormalized: aliases }).where(eq(players.id, player.id)).run();
  return "renamed";
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
  // A player whose own client named his server stays there: NC only confirms or enriches, it never moves him.
  const before = db.select().from(playerNcMatch).where(eq(playerNcMatch.playerId, playerId)).get();
  const clientServer = before?.status === "client" ? before.ncServerId : null;
  // A player NC confirmed before is asked for by his character id: a rename changes the name, never the id, and a search by the old name would lose him.
  // `suspect`: what we hold about him is in doubt (the id is gone, or the class does not fit what the game shows); if he is not confirmed again as the same character, his NC data goes.
  let suspect = false;
  let disproved: string | null = null;
  if (before?.status === "verified" && before.ncCharacterId && before.ncServerId) {
    try {
      const current = await fetchCharacterProfile(fetchFn, { characterId: before.ncCharacterId, serverId: before.ncServerId }, shard);
      const shown = subjectOf(player).className;
      if (current && shown && current.classId !== null && AION2_CLASS_BY_ID[current.classId] !== shown) {
        // A character never changes class: the game shows another class than NC's page has for this id, so this id is not him.
        suspect = true;
        disproved = before.ncCharacterId;
      } else if (current) {
        const via = applyName(player, current.name, before.ncServerId) ?? before.via;
        record(playerId, "verified", { ncServerId: before.ncServerId, ncRace: before.ncRace, ncCharacterId: before.ncCharacterId, via, candidates: before.candidates }, now);
        return "verified";
      }
    } catch {
      record(playerId, "error", { ncServerId: before.ncServerId, ncRace: before.ncRace, ncCharacterId: before.ncCharacterId, via: before.via, candidates: before.candidates, nextTryAt: now + MATCH_RETRY_AFTER.error }, now);
      return "error";
    }
    // NC does not know that id any more (character deleted or merged away), or it is not him: fall back to the name search below.
    suspect = true;
  }
  const subject = subjectOf(player);
  const race = subject.faction === "Elyos" ? 1 : subject.faction === "Asmodian" ? 2 : undefined;
  let hits: Hit[];
  let decision: Decision;
  try {
    hits = await searchHits(fetchFn, player.name, shard, { race, serverId: clientServer ?? undefined });
    if (clientServer !== null) {
      hits = hits.filter((h) => h.serverId === clientServer);
    }
    if (disproved) {
      hits = hits.filter((h) => decodeCharacterId(h.characterId) !== disproved);
    }
    decision = decide(hits, subject);
    if (decision.status === "ambiguous" && decision.remaining) {
      const byLegion = await decideByLegion(fetchFn, decision.remaining, subject, shard);
      if (byLegion) {
        decision = { status: "verified", hit: byLegion, via: "legion-name", candidates: decision.candidates };
      }
    }
  } catch {
    record(playerId, "error", clientServer === null ? {} : { ncServerId: clientServer, nextTryAt: now + MATCH_RETRY_AFTER.error }, now);
    return "error";
  }
  if (decision.status === "verified" && decision.hit) {
    const outcome = applyHit(player, decision.hit);
    if (outcome === "conflict") {
      record(playerId, "conflict", { ncServerId: decision.hit.serverId, ncRace: decision.hit.race, candidates: decision.candidates }, now);
      return "conflict";
    }
    const found = decodeCharacterId(decision.hit.characterId);
    if (suspect && found !== before?.ncCharacterId) {
      dropNcData(playerId); // another character: the data of the old one must not stay
    }
    record(playerId, "verified", { ncServerId: decision.hit.serverId, ncRace: decision.hit.race, ncCharacterId: found, via: decision.via, candidates: decision.candidates }, now);
    enrichPlayer(playerId);
    return "verified";
  }
  if (suspect) {
    dropNcData(playerId);
  }
  if (clientServer !== null) {
    // NC does not know him (yet) or cannot tell: his client's word still counts, ask again later.
    record(playerId, "client", { ncServerId: clientServer, candidates: decision.candidates }, now);
    return "client";
  }
  record(playerId, decision.status, { candidates: decision.candidates }, now);
  return decision.status;
}

/** A server the player's own client named: as good as a match, but NC is still asked later (low priority, see duePlayers) to confirm him and fill in his character. */
export function noteClientServer(playerId: number, ncServerId: number, now = Date.now()): void {
  const before = db.select().from(playerNcMatch).where(eq(playerNcMatch.playerId, playerId)).get();
  if ((before?.status === "verified" || before?.status === "client") && before.ncServerId === ncServerId) {
    return;
  }
  record(playerId, "client", { ncServerId, nextTryAt: now }, now);
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

/** The players that were never checked or whose result ran out, newest first; players their own client named come last. */
export function duePlayers(limit: number, now = Date.now()): number[] {
  const rows = db
    .select({ id: players.id, serverName: servers.displayName, nextTryAt: playerNcMatch.nextTryAt })
    .from(players)
    .innerJoin(servers, eq(players.serverId, servers.id))
    .leftJoin(playerNcMatch, eq(playerNcMatch.playerId, players.id))
    .where(sql`${servers.fingerprint} like 'aion2:%' and (${playerNcMatch.nextTryAt} is null or ${playerNcMatch.nextTryAt} <= ${now})`)
    .orderBy(sql`${playerNcMatch.status} = 'client'`, sql`${playerNcMatch.nextTryAt} is not null`, sql`${players.lastSeenAt} desc`)
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

/** What the portrait/character fetch needs for a player NC has confirmed: his real server and race are known, so the search cannot pick a namesake. Null for players NC did not confirm or outside Europe (the character pages are searched there). */
export function enrichKeyOf(playerId: number): PlayerKey | null {
  const row = db
    .select({ name: players.name, serverName: servers.displayName, status: playerNcMatch.status, ncServerId: playerNcMatch.ncServerId, ncRace: playerNcMatch.ncRace, faction: players.faction })
    .from(players)
    .innerJoin(playerNcMatch, eq(playerNcMatch.playerId, players.id))
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where(eq(players.id, playerId))
    .get();
  if (!row || row.status !== "verified" || row.ncServerId === null || shardOf(row.serverName) !== "eu") {
    return null;
  }
  const faction = row.ncRace === 1 ? "Elyos" : row.ncRace === 2 ? "Asmodian" : row.faction === "Elyos" || row.faction === "Asmodian" ? row.faction : null;
  return { playerId, name: row.name, faction, ownServerId: row.ncServerId };
}

/** Puts a confirmed player into the portrait queue (ahead of the background sweep: a new player is wanted at once), which fetches his portrait and character data (the Character tab). Never throws. */
export function enrichPlayer(playerId: number, queue: PortraitQueue = portraitQueue, priority: Priority = PRIORITY.upload): boolean {
  if (process.env.PORTRAITS_OFF === "1" || (!matchEnabled() && queue === portraitQueue)) {
    return false;
  }
  try {
    const key = enrichKeyOf(playerId);
    return key ? queue.enqueue(key, { priority }) : false;
  } catch {
    return false;
  }
}

/** Confirmed players whose portrait or character data is missing or has run out, newest first (the Character tab is filled without anybody opening the profile). */
export function enrichDue(limit: number, now = Date.now()): number[] {
  return db
    .select({ id: players.id })
    .from(players)
    .innerJoin(playerNcMatch, eq(playerNcMatch.playerId, players.id))
    .innerJoin(servers, eq(players.serverId, servers.id))
    .leftJoin(playerPortraits, eq(playerPortraits.playerId, players.id))
    .leftJoin(playerNcCharacter, eq(playerNcCharacter.playerId, players.id))
    .where(
      sql`${playerNcMatch.status} = 'verified' and ${servers.displayName} like 'Europe - %'
        and (${playerPortraits.nextTryAt} is null or ${playerPortraits.nextTryAt} <= ${now} or ${playerNcCharacter.nextTryAt} is null or ${playerNcCharacter.nextTryAt} <= ${now})`,
    )
    .orderBy(sql`${players.lastSeenAt} desc`)
    .limit(limit)
    .all()
    .map((r) => r.id);
}

/** Background sweep: fills the queue up to its limit with confirmed players that lack portrait or character data. The first rounds fill everyone in one go, later ones only the new and the expired. */
export function startEnrichSweep(intervalMs = 5 * 60_000, batch = 150): void {
  if (!matchEnabled() || process.env.PORTRAITS_OFF === "1") {
    return;
  }
  const sweep = () => {
    try {
      for (const id of enrichDue(batch)) {
        if (!enrichPlayer(id, portraitQueue, PRIORITY.background) && portraitQueue.length >= 150) {
          break;
        }
      }
    } catch {
      // try again at the next round
    }
  };
  setTimeout(sweep, 120_000).unref();
  setInterval(sweep, intervalMs).unref();
}

/**
 * After an upload: the confirmed players it names are looked at again when the last look is older than twelve hours (an upload shows he is
 * playing now), or when a client reports a higher level than NC's page had (he has levelled up). A class that differs from NC's page sends the player to the match check at once: the name no longer stands for the same character. Players without data yet are fetched as
 * well. Jobs go ahead of the background sweep; the look itself is cheap (see processPlayer). Never throws.
 */
export function refreshAfterUpload(entries: { id: number; level?: number; classId?: number }[], queue: PortraitQueue = portraitQueue, matches: MatchQueue = matchQueue, now = Date.now()): number {
  if (process.env.PORTRAITS_OFF === "1" || (!matchEnabled() && queue === portraitQueue)) {
    return 0;
  }
  let queued = 0;
  for (const { id, level, classId } of entries) {
    try {
      const key = enrichKeyOf(id);
      if (!key) {
        continue;
      }
      const row = db.select({ fetchedAt: playerNcCharacter.fetchedAt, status: playerNcCharacter.status, ncLevel: playerNcCharacter.ncLevel, ncClassId: playerNcCharacter.ncClassId }).from(playerNcCharacter).where(eq(playerNcCharacter.playerId, id)).get();
      if (row?.ncClassId != null && classId !== undefined && classId !== row.ncClassId) {
        // A character never changes class: the name now stands for another character, or the match was wrong. Check the match again, now.
        matches.enqueue([id]);
        continue;
      }
      const levelledUp = row?.ncLevel != null && level !== undefined && level > row.ncLevel;
      const stale = !row || row.status !== "ok" || now - row.fetchedAt > EVENT_STALE_AFTER.upload;
      if ((stale || levelledUp) && queue.enqueue(key, { priority: PRIORITY.upload, force: true })) {
        queued++;
      }
    } catch {
      // the next upload tries again
    }
  }
  return queued;
}
