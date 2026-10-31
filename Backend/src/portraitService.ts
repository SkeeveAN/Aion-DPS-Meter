import { AsyncLocalStorage } from "node:async_hooks";
import { mkdirSync, renameSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { and, eq, isNull } from "drizzle-orm";
import { db } from "./db/client.js";
import { playerNcCharacter, playerNcMatch, playerPortraits, playerProfiles, players } from "./db/schema.js";
import { mapDaevanionBoard, mapNcCharacter, openBoardIds, signatureOf, type NcCharacterData } from "./ncCharacter.js";
import { upsertNcProfile } from "./profile.js";
import { reconcileGuildWithNc } from "./guilds.js";
import { EVENT_STALE_AFTER, PRIORITY, characterInterval, parseSeen, portraitInterval, type Priority } from "./refreshPolicy.js";
import { portraitFromJpeg } from "./portrait.js";
import { NcPacing } from "./ncPacing.js";

// Official character portrait and character data (attributes, wing, pet) of a player from NC's public character search, profile image
// and character page, shown on the player page.
// Be polite to NC: only profiles somebody opens, one request at a time with a pause between them, results are cached for days.

type FetchFn = typeof fetch;

export const PORTRAIT_DIR = path.resolve(process.env.PORTRAIT_DIR ?? "./data/portraits");

export const SEARCH_URL = "https://api-search.plaync.com/aion2global/search/v2/character";
const IMAGE_URL = "https://profileimg.plaync.com/game_profile_images/aion2global/images";
const BROWSER_HEADERS = {
  "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
  Origin: "https://aion2.plaync.com",
  Referer: "https://aion2.plaync.com/",
};
const TW_HOST = "tw.ncsoft.com";
const TW_HEADERS = { ...BROWSER_HEADERS, Origin: `https://${TW_HOST}`, Referer: `https://${TW_HOST}/aion2/` };
const INFO_URL = "https://aion2.plaync.com/api/character/info";
const EQUIPMENT_URL = "https://aion2.plaync.com/api/character/equipment";
const DAEVANION_URL = "https://aion2.plaync.com/api/character/daevanion/detail";
// The character pages are slower than the search (one complete run once ended with an error after ~9.6 s at 10 s).
const TIMEOUT_MS = 15_000;
const MAX_IMAGE_BYTES = 2 * 1024 * 1024;
export const MAX_SEARCH_BYTES = 256 * 1024;
const MAX_CHARACTER_BYTES = 512 * 1024;

export const HOUR = 3_600_000;
export const DAY = 24 * HOUR;
/** When to try again, by outcome: a picture can change with the character's looks, "not found" may be a typo or a new character, errors are transient. */
export const RETRY_AFTER = { ok: 7 * DAY, none: 2 * DAY, error: 1 * HOUR } as const;
/** The same for the character data: the values change faster than the looks, so a refresh after 3 days. */
export const CHARACTER_RETRY_AFTER = { ok: 3 * DAY, none: 2 * DAY, error: 1 * HOUR } as const;
export type PortraitStatus = keyof typeof RETRY_AFTER;

/** The combat power the 10 against 10 arena gives everybody (the game evens the players out there; NC's page shows it while they stand in it, and skills at level 20). */
export const ARENA_FIXED_COMBAT_POWER = 397228;
/** NC's page of a player that stands in the evened-out arena: nothing on it is his real state, so none of it is stored; the look is repeated soon. */
export const isArenaEqualized = (data: { combatPower: number | null } | null | undefined): boolean => data?.combatPower === ARENA_FIXED_COMBAT_POWER;
export const ARENA_RETRY_AFTER = 15 * 60_000;

/** `region` is NC's region key (eu, as, nae, naw, la) of the player's server; "eu" when left out. */
export type PlayerKey = { playerId: number; name: string; faction: "Elyos" | "Asmodian" | null; ownServerId: number | null; region?: string };
/** characterId is NC's id for the character pages (url-decoded base64url), null when the search hit had none or a strange one. */
export type Character = { charKey: string; serverId: number; characterId: string | null };
/** What the character pages need: found by a search before, stored in player_nc_character. */
export type KnownCharacter = { characterId: string; serverId: number };

/**
 * NC runs the game in three places with the same kind of answers: Global (the shards eu, as, nae, naw, la), Korea ("kr") and Taiwan ("tw"). Korea and Taiwan have their own
 * search and character pages (found by trying them live, 31.10.2026: the Korean search lives under /ko-kr/, its character pages are the global ones; Taiwan has all four under tw.ncsoft.com).
 * Both number their servers 1001-1018 / 2001-2018 (Korea beyond), so a name's hit says which of the two it is only through the region it was found in.
 */
export const OTHER_SHARDS = ["tw", "kr"] as const;
export const isOtherShard = (shard: string): boolean => (OTHER_SHARDS as readonly string[]).includes(shard);
const OTHER_ENDPOINTS: Record<string, { search: string; info: string; equipment: string; daevanion: string; lang: string }> = {
  kr: {
    search: "https://aion2.plaync.com/ko-kr/api/search/aion2/search/v2/character",
    info: INFO_URL,
    equipment: EQUIPMENT_URL,
    daevanion: DAEVANION_URL,
    lang: "ko-kr",
  },
  tw: {
    search: `https://${TW_HOST}/aion2/api/search/character`,
    info: `https://${TW_HOST}/aion2/api/character/info`,
    equipment: `https://${TW_HOST}/aion2/api/character/equipment`,
    daevanion: `https://${TW_HOST}/aion2/api/character/daevanion/detail`,
    lang: "zh-TW",
  },
};

/** The URL of a name search in a region, with the parameters that region understands (Global filters by `region`, Korea and Taiwan have one search each). */
export function searchUrlOf(shard: string, name: string, filter: { race?: 1 | 2; serverId?: number }, page: number): URL {
  const other = OTHER_ENDPOINTS[shard];
  const url = new URL(other ? other.search : SEARCH_URL);
  url.searchParams.set("keyword", name);
  if (!other) {
    url.searchParams.set("region", shard);
  }
  // NC filters on its side (a common name is a substring of many others: "Pixie" has 136 hits, 77 of them Elyos).
  if (filter.race) {
    url.searchParams.set("race", String(filter.race));
  }
  if (filter.serverId) {
    url.searchParams.set("serverId", String(filter.serverId));
  }
  if (!other) {
    url.searchParams.set("localeInfo", "en-US");
  }
  url.searchParams.set("page", String(page));
  url.searchParams.set("size", "40");
  return url;
}

const RACE_BY_FACTION = { Elyos: 1, Asmodian: 2 } as const;

const hostAllowed = (url: URL) => url.protocol === "https:" && (url.hostname.endsWith(".plaync.com") || url.hostname.endsWith(".playnccdn.com") || url.hostname === TW_HOST);

/** Reads a response body with a size limit. */
async function readLimited(res: Response, limit: number): Promise<Buffer> {
  const chunks: Buffer[] = [];
  let total = 0;
  for await (const part of res.body as unknown as AsyncIterable<Uint8Array>) {
    total += part.length;
    if (total > limit) {
      throw new Error("response too large");
    }
    chunks.push(Buffer.from(part));
  }
  return Buffer.concat(chunks);
}

/** GET with timeout; redirects are followed by hand and only to NC hosts. */
export async function getNc(fetchFn: FetchFn, start: URL, limit: number): Promise<{ res: Response; body: Buffer }> {
  let url = start;
  for (let hop = 0; hop < 4; hop++) {
    if (!hostAllowed(url)) {
      throw new Error(`host not allowed: ${url.hostname}`);
    }
    const res = await fetchFn(url, { headers: url.hostname === TW_HOST ? TW_HEADERS : BROWSER_HEADERS, redirect: "manual", signal: AbortSignal.timeout(TIMEOUT_MS) });
    if (res.status >= 300 && res.status < 400) {
      const location = res.headers.get("location");
      if (!location) {
        throw new Error("redirect without location");
      }
      url = new URL(location, url);
      continue;
    }
    if (!res.ok) {
      throw new Error(`HTTP ${res.status}`);
    }
    return { res, body: await readLimited(res, limit) };
  }
  throw new Error("too many redirects");
}

/** The search answer carries the character id url-encoded (base64url, "=" as %3D); returns it decoded, or null when it is not plausible. */
export function decodeCharacterId(raw: unknown): string | null {
  if (typeof raw !== "string" || raw.length > 300) {
    return null;
  }
  try {
    const id = decodeURIComponent(raw);
    return /^[A-Za-z0-9_-]{4,200}={0,2}$/.test(id) ? id : null;
  } catch {
    return null;
  }
}

const stripTags = (s: string) => s.replace(/<[^>]*>/g, "");

/**
 * Picks the one character a search answer is about: exact same name (tags removed, case-sensitive), the race of the player's
 * faction and a numeric character key. Several candidates -> the one on the player's own server if that settles it, else null
 * (never guess). `list` is the answer's `list` array, taken as untrusted data.
 */
export function pickCharacter(list: unknown, key: Pick<PlayerKey, "name" | "faction" | "ownServerId">): Character | null {
  if (!Array.isArray(list)) {
    return null;
  }
  const found: Character[] = [];
  for (const entry of list as Record<string, unknown>[]) {
    if (typeof entry?.name !== "string" || stripTags(entry.name) !== key.name) {
      continue;
    }
    if (key.faction && entry.race !== RACE_BY_FACTION[key.faction]) {
      continue;
    }
    const serverId = Number(entry.serverId);
    const charKey = /[?&]charKey=(\d{1,20})(?:&|$)/.exec(String(entry.profileImageUrl ?? ""))?.[1];
    if (!charKey || !Number.isInteger(serverId) || serverId < 1000 || serverId > 9999) {
      continue;
    }
    found.push({ charKey, serverId, characterId: decodeCharacterId(entry.characterId) });
  }
  if (found.length === 1) {
    return found[0];
  }
  const own = found.filter((c) => c.serverId === key.ownServerId);
  return own.length === 1 ? own[0] : null;
}

/** Name search at NC; resolves to the character or null (nothing, or not unambiguous). Network/HTTP problems throw. */
export async function searchCharacter(fetchFn: FetchFn, key: Pick<PlayerKey, "name" | "faction" | "ownServerId" | "region">): Promise<Character | null> {
  const url = new URL(SEARCH_URL);
  url.searchParams.set("keyword", key.name);
  url.searchParams.set("region", key.region ?? "eu");
  url.searchParams.set("localeInfo", "en-US");
  url.searchParams.set("page", "1");
  url.searchParams.set("size", "40");
  const { body } = await getNc(fetchFn, url, MAX_SEARCH_BYTES);
  return pickCharacter((JSON.parse(body.toString("utf8")) as { list?: unknown }).list, key);
}

/** The profile image of a character as JPEG bytes. */
export async function downloadPortrait(fetchFn: FetchFn, character: Character): Promise<Buffer> {
  const url = new URL(IMAGE_URL);
  url.searchParams.set("gameServerKey", String(character.serverId));
  url.searchParams.set("charKey", character.charKey);
  const { res, body } = await getNc(fetchFn, url, MAX_IMAGE_BYTES);
  if (!(res.headers.get("content-type") ?? "").startsWith("image/")) {
    throw new Error("not an image");
  }
  return body;
}

/** True when a stored row is still good and nothing must be fetched. */
export function isFresh(row: { nextTryAt: number } | undefined, now: number): boolean {
  return row !== undefined && row.nextTryAt > now;
}

export const portraitFile = (playerId: number) => path.join(PORTRAIT_DIR, `${playerId}.png`);

export type Outcome = { status: PortraitStatus; character: Character | null; png: Buffer | null };

/** One NC JSON answer (character pages) as untrusted data. */
async function getNcJson(fetchFn: FetchFn, url: URL): Promise<unknown> {
  const { body } = await getNc(fetchFn, url, MAX_CHARACTER_BYTES);
  return JSON.parse(body.toString("utf8"));
}

function characterPageUrl(base: string, known: KnownCharacter, region = "eu"): URL {
  const other = OTHER_ENDPOINTS[region];
  const page = other ? (base === INFO_URL ? other.info : base === EQUIPMENT_URL ? other.equipment : other.daevanion) : base;
  const url = new URL(page);
  if (!other) {
    url.searchParams.set("region", region);
  }
  url.searchParams.set("lang", other ? other.lang : "en-US");
  url.searchParams.set("characterId", known.characterId);
  url.searchParams.set("serverId", String(known.serverId));
  return url;
}

export type NcProfile = { name: string; guild: string; level: number; classId: number | null };

/** Our class id of NC's pcId ((pcId - 1) / 4), null when it is no usable number. */
export function classIdOfPc(pcId: unknown): number | null {
  const n = Number(pcId);
  return Number.isInteger(n) && n >= 1 ? Math.floor((n - 1) / 4) : null;
}

/** The name, legion and level NC holds for a character id right now (one request); null when NC has no such character or the answer is unusable. Throws on network/HTTP problems. */
export async function fetchCharacterProfile(fetchFn: FetchFn, known: KnownCharacter, region = "eu"): Promise<NcProfile | null> {
  const info = (await getNcJson(fetchFn, characterPageUrl(INFO_URL, known, region))) as { profile?: Record<string, unknown> } | null;
  const p = info?.profile;
  if (!p || typeof p.characterName !== "string" || p.characterName === "") {
    return null;
  }
  return { name: stripTags(p.characterName), guild: typeof p.regionName === "string" ? stripTags(p.regionName) : "", level: Number(p.characterLevel) || 0, classId: classIdOfPc(p.pcId) };
}

/** What one info request tells: the cheap look at a character (see signatureOf), and the name NC holds for him now. */
export async function fetchCharacterLook(fetchFn: FetchFn, known: KnownCharacter, region = "eu"): Promise<{ data: NcCharacterData | null; name: string | null }> {
  const info = await getNcJson(fetchFn, characterPageUrl(INFO_URL, known, region));
  const name = (info as { profile?: { characterName?: unknown } } | null)?.profile?.characterName;
  return { data: mapNcCharacter(info, null), name: typeof name === "string" && name !== "" ? stripTags(name) : null };
}

/** A complete fetch is forced at least this often, whatever the cheap looks say (a swapped piece of equal value does not change the combat power). */
export const FULL_FETCH_AFTER = 30 * DAY;

/** The character data of one character: info + equipment + one request per Daevanion board with activated nodes. Throws on network/HTTP problems; null when the answer is unusable. */
export async function fetchCharacterData(fetchFn: FetchFn, known: KnownCharacter, region = "eu"): Promise<NcCharacterData | null> {
  const info = await getNcJson(fetchFn, characterPageUrl(INFO_URL, known, region));
  const equipment = await getNcJson(fetchFn, characterPageUrl(EQUIPMENT_URL, known, region));
  const data = mapNcCharacter(info, equipment);
  if (data) {
    // One request per board that has activated nodes: the profile page draws the boards from them like those of the player's own client.
    for (const boardId of openBoardIds(info)) {
      const url = characterPageUrl(DAEVANION_URL, known, region);
      url.searchParams.set("boardId", String(boardId));
      const board = mapDaevanionBoard(boardId, await getNcJson(fetchFn, url));
      if (board) {
        data.daevanion.push(board);
      }
    }
  }
  return data;
}

export type CharacterOutcome = { status: PortraitStatus; data: NcCharacterData | null };

/** One complete portrait attempt for a player: search, download, cut out. Never throws; failures become status 'error'. */
export async function fetchPortrait(fetchFn: FetchFn, key: PlayerKey): Promise<Outcome> {
  return (await fetchPlayer(fetchFn, key, { portrait: true, character: false }, null)).portrait as Outcome;
}

export type Needs = { portrait: boolean; character: boolean };
export type PlayerOutcome = {
  /** Set when the portrait was asked for. */
  portrait: Outcome | null;
  /** Set when the character data was asked for; `known` is the id pair it was (or would have been) fetched with. */
  character: (CharacterOutcome & { known: KnownCharacter | null }) | null;
};

/**
 * One complete attempt for a player, doing only what is due: the search (needed for the portrait, and for the character data
 * unless the character id is already known), the portrait download, info + equipment. Never throws; failures become status 'error'.
 * Several NC requests per player - the caller hands in a fetch that keeps the pause between requests.
 */
export async function fetchPlayer(fetchFn: FetchFn, key: PlayerKey, needs: Needs, known: KnownCharacter | null): Promise<PlayerOutcome> {
  let found: Character | null = null;
  if (needs.portrait || !known) {
    try {
      found = await searchCharacter(fetchFn, key);
    } catch {
      return {
        portrait: needs.portrait ? { status: "error", character: null, png: null } : null,
        character: needs.character ? { status: "error", data: null, known } : null,
      };
    }
    if (!found) {
      return {
        portrait: needs.portrait ? { status: "none", character: null, png: null } : null,
        character: needs.character ? { status: "none", data: null, known: null } : null,
      };
    }
  }
  let portrait: Outcome | null = null;
  if (needs.portrait && found) {
    try {
      portrait = { status: "ok", character: found, png: portraitFromJpeg(await downloadPortrait(fetchFn, found)) };
    } catch {
      portrait = { status: "error", character: null, png: null };
    }
  }
  let character: PlayerOutcome["character"] = null;
  if (needs.character) {
    const target: KnownCharacter | null = found?.characterId ? { characterId: found.characterId, serverId: found.serverId } : known;
    try {
      const data = target ? await fetchCharacterData(fetchFn, target, key.region) : null;
      character = { status: data ? "ok" : "error", data, known: target };
    } catch {
      character = { status: "error", data: null, known: target };
    }
  }
  return { portrait, character };
}

/**
 * Wraps a fetch so that every request starts at least `minGapMs` after the previous one, across all players: the global rate
 * limit towards NC (the queue alone only paces the jobs, and one job makes up to four requests). Time is injectable for tests.
 */
export function pacedFetch(
  fetchFn: FetchFn,
  minGapMs: number | (() => number),
  now: () => number = Date.now,
  sleep: (ms: number) => Promise<void> = (ms) => new Promise((r) => setTimeout(r, ms)),
): FetchFn {
  let last = -Infinity;
  return (async (input: Parameters<FetchFn>[0], init?: Parameters<FetchFn>[1]) => {
    // The slot is taken before sleeping: callers arriving together queue up one gap apart instead of all computing the same wait and firing at once.
    const startAt = Math.max(now(), last + (typeof minGapMs === "function" ? minGapMs() : minGapMs));
    last = startAt;
    const wait = startAt - now();
    if (wait > 0) {
      await sleep(wait);
    }
    return fetchFn(input, init);
  }) as FetchFn;
}

type Job = { key: PlayerKey; priority: Priority; force: boolean };

/** Player matches (ncMatch.ts) that the workers of the PortraitQueue take as well: one set of workers and one learned pace for everything asked of NC. */
export interface MatchSource {
  /** Players waiting. */
  readonly size: number;
  /** Whether the next one to take is from the bulk lane (true), the ordinary one (false), or nobody waits (null). */
  next(): { background: boolean } | null;
  take(): number | undefined;
  run(playerId: number): Promise<unknown>;
  /** Set by the queue that hosts the workers: told when players were added. */
  host: { wake(): void } | null;
}

/**
 * Work queue: one job at a time, a pause between jobs, bounded, no duplicates. Two buckets: the normal one (a profile somebody looks at, then a
 * player an upload named, in order of arrival within one priority) and the background one (the sweep, with its backlog of thousands). Normal jobs go first,
 * but after every NORMAL_PER_BACKGROUND of them one background job is taken, so the backlog always moves. `allow` tells whether the hourly budget towards NC
 * leaves room for a job of that priority; when it does not, the queue waits a minute. Time is injectable for tests.
 */
export class PortraitQueue {
  private readonly waiting = new Map<number, Job>();
  /** Workers that are running (each takes one player at a time). */
  private workers = 0;
  private readonly current = new Set<number>();
  private lastStart = 0;
  private matches: MatchSource | null = null;
  /** The crowd lookups of world boss uploads (crowdResolve.ts), taken by the same workers. */
  private crowd: MatchSource | null = null;
  /** Workers that are busy with a player match or a crowd lookup (these are not in `current`). */
  private activeMatches = 0;
  constructor(
    private readonly work: (key: PlayerKey, job: { force: boolean }) => Promise<void>,
    private readonly opts: { minGapMs: number; maxLength: number; now: () => number; sleep: (ms: number) => Promise<void>; allow?: (priority: Priority) => boolean; concurrency?: number; pausedUntil?: () => number } = {
      minGapMs: 2000,
      maxLength: 200,
      now: Date.now,
      sleep: (ms) => new Promise((r) => setTimeout(r, ms)),
    },
  ) {}

  get length(): number {
    return this.waiting.size;
  }

  /** From now on the workers also take the players of `source`: its own worker stops, these do the work. */
  attachMatches(source: MatchSource): void {
    this.matches = source;
    source.host = { wake: () => this.kick() };
    this.kick();
  }

  /** From now on the workers also take the crowd lookups of `source` (a world boss upload waits for them): same workers, same learned pace. */
  attachCrowd(source: MatchSource): void {
    this.crowd = source;
    source.host = { wake: () => this.kick() };
    this.kick();
  }

  /** What the workers are doing, for the log. */
  state(): { workers: number; waiting: number; current: number; activeMatches: number; matches: number; crowd: number } {
    return { workers: this.workers, waiting: this.waiting.size, current: this.current.size, activeMatches: this.activeMatches, matches: this.matches?.size ?? 0, crowd: this.crowd?.size ?? 0 };
  }

  private matchesWaiting(): number {
    return (this.matches?.size ?? 0) + (this.crowd?.size ?? 0);
  }

  /** false when the player is already queued (his job only moves up if this one is more urgent) or the queue is full. */
  enqueue(key: PlayerKey, how: { priority?: Priority; force?: boolean } = {}): boolean {
    const priority = how.priority ?? PRIORITY.background;
    const queued = this.waiting.get(key.playerId);
    if (queued) {
      queued.priority = Math.max(queued.priority, priority) as Priority;
      queued.force ||= how.force === true;
      return false;
    }
    if (this.current.has(key.playerId) || this.waiting.size + this.current.size >= this.opts.maxLength) {
      return false;
    }
    this.waiting.set(key.playerId, { key, priority, force: how.force === true });
    this.kick();
    return true;
  }

  /** Normal jobs (a profile somebody looks at, a player an upload named) served in a row while background jobs wait, before one background job gets its turn. */
  static readonly NORMAL_PER_BACKGROUND = 3;
  private normalStreak = 0;

  private hasViewJob(): boolean {
    for (const job of this.waiting.values()) {
      if (job.priority >= PRIORITY.view) {
        return true;
      }
    }
    return false;
  }

  private hasNormalJob(): boolean {
    for (const job of this.waiting.values()) {
      if (job.priority > PRIORITY.background) {
        return true;
      }
    }
    return false;
  }

  /** The jobs to take next, in the order to try them: the best normal job (by priority, then arrival) and the oldest background job - the background one first after NORMAL_PER_BACKGROUND normal ones. */
  private candidates(): [number, Job][] {
    let normal: [number, Job] | null = null;
    let background: [number, Job] | null = null;
    for (const entry of this.waiting) {
      if (entry[1].priority > PRIORITY.background) {
        if (normal === null || entry[1].priority > normal[1].priority) {
          normal = entry;
        }
      } else if (background === null) {
        background = entry;
      }
    }
    const order = background !== null && this.normalStreak >= PortraitQueue.NORMAL_PER_BACKGROUND ? [background, normal] : [normal, background];
    return order.filter((e): e is [number, Job] => e !== null);
  }

  /** Starts workers up to the concurrency while there are jobs they could take. */
  private kick(): void {
    const limit = Math.max(1, this.opts.concurrency ?? 1);
    // a worker that is still waiting for its start slot will take a waiting job itself: only more jobs than idle workers need a new one
    while (this.workers < limit && this.waiting.size + this.matchesWaiting() > this.workers - this.current.size - this.activeMatches) {
      this.workers++;
      void this.worker();
    }
  }

  private async worker(): Promise<void> {
    try {
      while (this.waiting.size > 0 || this.matchesWaiting() > 0) {
        // The start slot is taken before waiting: workers that come together start one gap apart.
        const startAt = Math.max(this.opts.now(), this.lastStart + this.opts.minGapMs);
        this.lastStart = startAt;
        const wait = startAt - this.opts.now();
        if (wait > 0) {
          await this.opts.sleep(wait);
        }
        if (this.waiting.size === 0 && this.matchesWaiting() === 0) {
          break;
        }
        // NC said "too many" (429/403): no job starts until the pause is over, so no player is marked as failed for it.
        const paused = (this.opts.pausedUntil?.() ?? 0) - this.opts.now();
        if (paused > 0) {
          await this.opts.sleep(paused);
          continue;
        }
        // The players of a world boss upload (the upload waits for all of them) go before the matches; a profile somebody looks at still goes first.
        // (it yields to a view only: the jobs of uploads are always there, and a world boss upload that waited behind them waited for hours)
        if (this.crowd && this.crowd.size > 0 && !this.hasViewJob() && (!this.opts.allow || this.opts.allow(PRIORITY.upload))) {
          const unit = this.crowd.take();
          if (unit !== undefined) {
            this.activeMatches++;
            try {
              await jobContext.run({ background: false }, () => this.crowd?.run(unit));
            } catch {
              // the lookup keeps its own failures
            }
            this.activeMatches--;
            continue;
          }
        }
        // A player match goes before the sweep's backlog (it settles who the player is, his runs wait for it), but not before a profile somebody looks at.
        const match = this.matches?.next();
        if (match && (match.background ? !this.hasNormalJob() : true) && (!this.opts.allow || this.opts.allow(match.background ? PRIORITY.background : PRIORITY.upload))) {
          const id = this.matches?.take();
          if (id !== undefined) {
            this.activeMatches++;
            try {
              await jobContext.run({ background: match.background }, () => this.matches?.run(id));
            } catch {
              // the match records its own failures
            }
            this.activeMatches--;
          }
          continue;
        }
        // The first job of the order the budget lets through; none: the hourly budget is used up for them, look again in a minute.
        const chosen = this.candidates().find(([, j]) => !this.opts.allow || this.opts.allow(j.priority));
        if (!chosen) {
          await this.opts.sleep(60_000);
          continue;
        }
        const [id, job] = chosen;
        this.normalStreak = job.priority === PRIORITY.background ? 0 : this.normalStreak + 1;
        this.waiting.delete(id);
        this.current.add(id);
        try {
          await jobContext.run({ background: job.priority === PRIORITY.background }, () => this.work(job.key, { force: job.force }));
        } catch {
          // the worker records its own failures; one bad job must not stop the queue
        }
        this.current.delete(id);
      }
    } finally {
      this.workers--;
    }
  }
}

/** The job that is running in this async chain, so its requests are counted for the right budget even while several jobs run at once. */
const jobContext = new AsyncLocalStorage<{ background: boolean }>();

/** The one rate limit towards NC, shared by everything that asks it (portraits, character data, the player match). */
export const ncPacing = new NcPacing();
// The timeout starts only when the request really goes out: the caller's one ran during the wait in the queue, so at a wide gap the later workers
// timed out before they were sent, which counted as network errors, kept the gap wide and failed every request.
const paced = pacedFetch((input, init) => fetch(input, { ...init, signal: AbortSignal.timeout(TIMEOUT_MS) }), () => ncPacing.gapMs());
const requestTimes: number[] = [];
/** The same for the requests of background jobs alone: they have a share of their own (see withinBudget). */
const backgroundRequestTimes: number[] = [];
function lastHour(times: number[], now: number): number {
  while (times.length > 0 && times[0] <= now - HOUR) {
    times.shift();
  }
  return times.length;
}
/** Requests towards NC in the last hour (all of them: portraits, character data, the player match). */
export function requestsLastHour(now = Date.now()): number {
  return lastHour(requestTimes, now);
}
/** The part of those that background jobs made. */
export function backgroundRequestsLastHour(now = Date.now()): number {
  return lastHour(backgroundRequestTimes, now);
}
/** The share of the hourly budget that is the background's own: the normal jobs cannot use it up (a quarter of NC_BUDGET_PER_HOUR). */
export const BACKGROUND_SHARE = 0.25;
/**
 * Hourly budget by priority (NC_BUDGET_PER_HOUR, default 9000): profiles somebody looks at may use up to 1.5 times it, a player an upload named 1.3 times,
 * the background sweep the budget itself - and, whatever the others have used, its own share (BACKGROUND_SHARE) up to the ceiling of 1.5 times, so it
 * keeps moving while there is traffic from views and uploads.
 */
export function withinBudget(priority: Priority, now = Date.now()): boolean {
  const base = Math.max(1, Number(process.env.NC_BUDGET_PER_HOUR ?? 9000) || 9000);
  const total = requestsLastHour(now);
  if (priority >= PRIORITY.view) {
    return total < Math.round(base * 1.5);
  }
  if (priority >= PRIORITY.upload) {
    return total < Math.round(base * 1.3);
  }
  return total < base || (backgroundRequestsLastHour(now) < base * BACKGROUND_SHARE && total < Math.round(base * 1.5));
}
export const ncFetch = (async (input: Parameters<FetchFn>[0], init?: Parameters<FetchFn>[1]) => {
  // After a 429/403 nobody asks for a while.
  const pause = ncPacing.pausedUntil() - Date.now();
  if (pause > 0) {
    await new Promise((r) => setTimeout(r, pause));
  }
  const at = Date.now();
  requestTimes.push(at);
  if (jobContext.getStore()?.background) {
    backgroundRequestTimes.push(at);
  }
  try {
    const res = await paced(input, init);
    ncPacing.note(res.status);
    return res;
  } catch (e) {
    ncPacing.note("network");
    throw e;
  }
}) as FetchFn;

/**
 * Does what is due for a player (portrait and/or character data), writes the file and records both statuses.
 * Fetch and clock are injectable for tests.
 */
export async function processPlayer(key: PlayerKey, fetchFn: FetchFn = ncFetch, now = Date.now(), job: { force?: boolean } = {}): Promise<void> {
  const portraitBefore = db.select().from(playerPortraits).where(eq(playerPortraits.playerId, key.playerId)).get();
  const characterBefore = db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, key.playerId)).get();
  // A forced job (an upload named the player, somebody looks at his profile) looks at the character data now; the picture keeps its own times.
  const needs = { portrait: !isFresh(portraitBefore, now), character: job.force === true || !isFresh(characterBefore, now) };
  if (!needs.portrait && !needs.character) {
    return;
  }
  const lastSeen = parseSeen(db.select({ seen: players.lastSeenAt }).from(players).where(eq(players.id, key.playerId)).get()?.seen);
  const ownClient = db.select({ source: playerProfiles.source }).from(playerProfiles).where(eq(playerProfiles.playerId, key.playerId)).get()?.source === "self";
  // The id pair the character check stored is as good as an earlier fetch's: no search needed for the character data.
  const matched = db.select({ characterId: playerNcMatch.ncCharacterId, serverId: playerNcMatch.ncServerId }).from(playerNcMatch).where(eq(playerNcMatch.playerId, key.playerId)).get();
  const known = characterBefore?.characterId && characterBefore.ncServerId
    ? { characterId: characterBefore.characterId, serverId: characterBefore.ncServerId }
    : matched?.characterId && matched.serverId ? { characterId: matched.characterId, serverId: matched.serverId } : null;

  // The cheap look first: one request (the info answer). When it finds what the last complete fetch found, nothing else is fetched.
  if (needs.character && known && characterBefore?.status === "ok" && characterBefore.probeJson && characterBefore.fullAt !== null && now - characterBefore.fullAt < FULL_FETCH_AFTER) {
    try {
      const look = await fetchCharacterLook(fetchFn, known, key.region);
      if (look.name && look.name !== key.name) {
        // He has been renamed: the match check looks him up by id and renames the row.
        db.update(playerNcMatch).set({ nextTryAt: 0 }).where(eq(playerNcMatch.playerId, key.playerId)).run();
      }
      if (look.data && signatureOf(look.data) === characterBefore.probeJson) {
        const unchanged = characterBefore.unchangedCount + 1;
        db.update(playerNcCharacter)
          .set({ fetchedAt: now, unchangedCount: unchanged, nextTryAt: now + characterInterval(lastSeen, unchanged, now, ownClient) })
          .where(eq(playerNcCharacter.playerId, key.playerId))
          .run();
        needs.character = false;
      }
    } catch {
      // the complete fetch below reports the problem
    }
    if (!needs.portrait && !needs.character) {
      return;
    }
  }
  const outcome = await fetchPlayer(fetchFn, key, needs, known);

  const p = outcome.portrait;
  if (p) {
    if (p.status === "ok" && p.png) {
      mkdirSync(PORTRAIT_DIR, { recursive: true });
      const target = portraitFile(key.playerId);
      writeFileSync(`${target}.tmp`, p.png);
      renameSync(`${target}.tmp`, target);
    }
    // A failed refresh keeps the old picture on show and only waits for the next try.
    const values = portraitBefore?.status === "ok" && p.status === "error"
      ? { ...portraitBefore, nextTryAt: now + RETRY_AFTER.error }
      : {
          charKey: p.character?.charKey ?? null,
          ncServerId: p.character?.serverId ?? null,
          status: p.status,
          fetchedAt: now,
          nextTryAt: now + (p.status === "ok" ? portraitInterval(lastSeen, now) : RETRY_AFTER[p.status]),
        };
    db.insert(playerPortraits).values({ playerId: key.playerId, ...values }).onConflictDoUpdate({ target: playerPortraits.playerId, set: values }).run();
  }

  const c = outcome.character;
  if (c) {
    // Data and its time stay on show when a later refresh fails or finds nothing; only the next try moves.
    const equalized = c.status === "ok" && isArenaEqualized(c.data);
    const values = c.status === "ok" && c.data && c.known && !equalized
      ? {
          characterId: c.known.characterId,
          ncServerId: c.known.serverId,
          attributesJson: JSON.stringify(c.data.attributes),
          wingId: c.data.wingId,
          wingSkinId: c.data.wingSkinId,
          activePet: c.data.activePet,
          activePetLevel: c.data.activePetLevel,
          combatPower: c.data.combatPower,
          itemLevel: c.data.itemLevel,
          ncGuild: c.data.guild || null,
          ncLevel: c.data.level,
          ncClassId: c.data.classId,
          probeJson: signatureOf(c.data),
          // A complete fetch that finds what the last one found counts as a look without news; a change starts over with the short wait.
          unchangedCount: characterBefore?.probeJson === signatureOf(c.data) ? characterBefore.unchangedCount + 1 : 0,
          fullAt: now,
          status: "ok",
          fetchedAt: now,
          nextTryAt: now + characterInterval(lastSeen, characterBefore?.probeJson === signatureOf(c.data) ? characterBefore.unchangedCount + 1 : 0, now, ownClient),
        }
      : {
          characterId: c.known?.characterId ?? characterBefore?.characterId ?? null,
          ncServerId: c.known?.serverId ?? characterBefore?.ncServerId ?? null,
          attributesJson: characterBefore?.attributesJson ?? "{}",
          wingId: characterBefore?.wingId ?? null,
          wingSkinId: characterBefore?.wingSkinId ?? null,
          activePet: characterBefore?.activePet ?? null,
          activePetLevel: characterBefore?.activePetLevel ?? null,
          combatPower: characterBefore?.combatPower ?? null,
          itemLevel: characterBefore?.itemLevel ?? null,
          ncGuild: characterBefore?.ncGuild ?? null,
          ncLevel: characterBefore?.ncLevel ?? null,
          ncClassId: characterBefore?.ncClassId ?? null,
          probeJson: characterBefore?.probeJson ?? null,
          unchangedCount: characterBefore?.unchangedCount ?? 0,
          fullAt: characterBefore?.fullAt ?? null,
          status: equalized ? (characterBefore?.status ?? "error") : c.status,
          fetchedAt: characterBefore?.fetchedAt ?? now,
          nextTryAt: now + (equalized ? ARENA_RETRY_AFTER : CHARACTER_RETRY_AFTER[c.status]),
        };
    db.insert(playerNcCharacter).values({ playerId: key.playerId, ...values }).onConflictDoUpdate({ target: playerNcCharacter.playerId, set: values }).run();
    if (c.status === "ok" && c.data && !equalized) {
      upsertNcProfile(key.playerId, c.data);
    }
    // NC's legion is the better word (see reconcileGuildWithNc); also moves a player who really changed legion.
    if (c.status === "ok") {
      reconcileGuildWithNc(key.playerId);
    }
    // A character found by the fetch's own search names the confirmed player's id as well (the check may predate the id column).
    if (c.status === "ok" && c.known) {
      db.update(playerNcMatch).set({ ncCharacterId: c.known.characterId }).where(and(eq(playerNcMatch.playerId, key.playerId), eq(playerNcMatch.status, "verified"), eq(playerNcMatch.ncServerId, c.known.serverId), isNull(playerNcMatch.ncCharacterId))).run();
    }
  }
}

/** Players fetched at the same time (NC_WORKERS, default 4); the pace of all requests together is ncPacing's. */
const WORKERS = Math.min(8, Math.max(1, Number(process.env.NC_WORKERS ?? 4) || 4));
export const portraitQueue = new PortraitQueue((key, job) => processPlayer(key, ncFetch, Date.now(), job), {
  minGapMs: 250,
  maxLength: 200,
  now: Date.now,
  sleep: (ms) => new Promise((r) => setTimeout(r, ms)),
  allow: (priority) => withinBudget(priority),
  concurrency: WORKERS,
  pausedUntil: () => ncPacing.pausedUntil(),
});

/**
 * The portrait URL for a player page, or null while there is no picture. Queues a fetch when the portrait or the character data
 * has no valid entry (never fetches in the request). A stale 'ok' keeps showing its old data while the new one is fetched.
 */
export function portraitUrlFor(key: PlayerKey, now = Date.now(), queue: PortraitQueue = portraitQueue): string | null {
  const row = db.select().from(playerPortraits).where(eq(playerPortraits.playerId, key.playerId)).get();
  const characterRow = db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, key.playerId)).get();
  if (process.env.PORTRAITS_OFF !== "1") {
    // Somebody looks at this profile: it goes to the front, and when the last look is older than six hours the character data is looked at again now.
    const looked = characterRow?.status === "ok" && now - characterRow.fetchedAt > EVENT_STALE_AFTER.view;
    if (!isFresh(row, now) || !isFresh(characterRow, now) || looked) {
      queue.enqueue(key, { priority: PRIORITY.view, force: looked });
    }
  }
  return row?.status === "ok" ? `/api/players/${key.playerId}/portrait?v=${row.fetchedAt}` : null;
}

/** Forgets everything NC-derived about a player: his data, his picture and a profile built from NC's page. For a name that turned out to stand for another character. */
export function dropNcData(playerId: number): void {
  db.delete(playerNcCharacter).where(eq(playerNcCharacter.playerId, playerId)).run();
  db.delete(playerPortraits).where(eq(playerPortraits.playerId, playerId)).run();
  db.delete(playerProfiles).where(and(eq(playerProfiles.playerId, playerId), eq(playerProfiles.source, "nc"))).run();
  rmSync(portraitFile(playerId), { force: true });
}
