import { mkdirSync, renameSync, writeFileSync } from "node:fs";
import path from "node:path";
import { eq } from "drizzle-orm";
import { db } from "./db/client.js";
import { playerNcCharacter, playerPortraits } from "./db/schema.js";
import { mapNcCharacter, type NcCharacterData } from "./ncCharacter.js";
import { portraitFromJpeg } from "./portrait.js";

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
const INFO_URL = "https://aion2.plaync.com/api/character/info";
const EQUIPMENT_URL = "https://aion2.plaync.com/api/character/equipment";
// The character pages are slower than the search (one complete run once ended with an error after ~9.6 s at 10 s).
const TIMEOUT_MS = 15_000;
const MAX_IMAGE_BYTES = 2 * 1024 * 1024;
export const MAX_SEARCH_BYTES = 256 * 1024;
const MAX_CHARACTER_BYTES = 256 * 1024;

export const HOUR = 3_600_000;
export const DAY = 24 * HOUR;
/** When to try again, by outcome: a picture can change with the character's looks, "not found" may be a typo or a new character, errors are transient. */
export const RETRY_AFTER = { ok: 7 * DAY, none: 2 * DAY, error: 1 * HOUR } as const;
/** The same for the character data: the values change faster than the looks, so a refresh after 3 days. */
export const CHARACTER_RETRY_AFTER = { ok: 3 * DAY, none: 2 * DAY, error: 1 * HOUR } as const;
export type PortraitStatus = keyof typeof RETRY_AFTER;

export type PlayerKey = { playerId: number; name: string; faction: "Elyos" | "Asmodian" | null; ownServerId: number | null };
/** characterId is NC's id for the character pages (url-decoded base64url), null when the search hit had none or a strange one. */
export type Character = { charKey: string; serverId: number; characterId: string | null };
/** What the character pages need: found by a search before, stored in player_nc_character. */
export type KnownCharacter = { characterId: string; serverId: number };

const RACE_BY_FACTION = { Elyos: 1, Asmodian: 2 } as const;

const hostAllowed = (url: URL) => url.protocol === "https:" && (url.hostname.endsWith(".plaync.com") || url.hostname.endsWith(".playnccdn.com"));

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
    const res = await fetchFn(url, { headers: BROWSER_HEADERS, redirect: "manual", signal: AbortSignal.timeout(TIMEOUT_MS) });
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
export async function searchCharacter(fetchFn: FetchFn, key: Pick<PlayerKey, "name" | "faction" | "ownServerId">): Promise<Character | null> {
  const url = new URL(SEARCH_URL);
  url.searchParams.set("keyword", key.name);
  url.searchParams.set("region", "eu");
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

function characterPageUrl(base: string, known: KnownCharacter): URL {
  const url = new URL(base);
  url.searchParams.set("region", "eu");
  url.searchParams.set("lang", "en-US");
  url.searchParams.set("characterId", known.characterId);
  url.searchParams.set("serverId", String(known.serverId));
  return url;
}

/** The character data of one character: info + equipment (two requests). Throws on network/HTTP problems; null when the answer is unusable. */
export async function fetchCharacterData(fetchFn: FetchFn, known: KnownCharacter): Promise<NcCharacterData | null> {
  const info = await getNcJson(fetchFn, characterPageUrl(INFO_URL, known));
  const equipment = await getNcJson(fetchFn, characterPageUrl(EQUIPMENT_URL, known));
  return mapNcCharacter(info, equipment);
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
      const data = target ? await fetchCharacterData(fetchFn, target) : null;
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
  minGapMs: number,
  now: () => number = Date.now,
  sleep: (ms: number) => Promise<void> = (ms) => new Promise((r) => setTimeout(r, ms)),
): FetchFn {
  let last = -Infinity;
  return (async (input: Parameters<FetchFn>[0], init?: Parameters<FetchFn>[1]) => {
    const wait = last + minGapMs - now();
    if (wait > 0) {
      await sleep(wait);
    }
    last = now();
    return fetchFn(input, init);
  }) as FetchFn;
}

/** Serial work queue: one job at a time, a pause between jobs, bounded, no duplicates. Time is injectable for tests. */
export class PortraitQueue {
  private readonly waiting = new Map<number, PlayerKey>();
  private running = false;
  private current: number | null = null;
  private lastStart = 0;
  constructor(
    private readonly work: (key: PlayerKey) => Promise<void>,
    private readonly opts: { minGapMs: number; maxLength: number; now: () => number; sleep: (ms: number) => Promise<void> } = {
      minGapMs: 2000,
      maxLength: 200,
      now: Date.now,
      sleep: (ms) => new Promise((r) => setTimeout(r, ms)),
    },
  ) {}

  get length(): number {
    return this.waiting.size;
  }

  /** false when the player is already queued or the queue is full. */
  enqueue(key: PlayerKey): boolean {
    if (this.current === key.playerId || this.waiting.has(key.playerId) || this.waiting.size + (this.current === null ? 0 : 1) >= this.opts.maxLength) {
      return false;
    }
    this.waiting.set(key.playerId, key);
    void this.drain();
    return true;
  }

  private async drain(): Promise<void> {
    if (this.running) {
      return;
    }
    this.running = true;
    try {
      while (this.waiting.size > 0) {
        const wait = this.lastStart + this.opts.minGapMs - this.opts.now();
        if (wait > 0) {
          await this.opts.sleep(wait);
        }
        const [id, key] = this.waiting.entries().next().value as [number, PlayerKey];
        this.waiting.delete(id);
        this.current = id;
        this.lastStart = this.opts.now();
        try {
          await this.work(key);
        } catch {
          // the worker records its own failures; one bad job must not stop the queue
        }
        this.current = null;
      }
    } finally {
      this.running = false;
    }
  }
}

/** The one rate limit towards NC, shared by everything that asks it (portraits, character data, the player match). */
export const ncFetch = pacedFetch(fetch, 2000);

/**
 * Does what is due for a player (portrait and/or character data), writes the file and records both statuses.
 * Fetch and clock are injectable for tests.
 */
export async function processPlayer(key: PlayerKey, fetchFn: FetchFn = ncFetch, now = Date.now()): Promise<void> {
  const portraitBefore = db.select().from(playerPortraits).where(eq(playerPortraits.playerId, key.playerId)).get();
  const characterBefore = db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, key.playerId)).get();
  const needs = { portrait: !isFresh(portraitBefore, now), character: !isFresh(characterBefore, now) };
  if (!needs.portrait && !needs.character) {
    return;
  }
  const known = characterBefore?.characterId && characterBefore.ncServerId ? { characterId: characterBefore.characterId, serverId: characterBefore.ncServerId } : null;
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
          nextTryAt: now + RETRY_AFTER[p.status],
        };
    db.insert(playerPortraits).values({ playerId: key.playerId, ...values }).onConflictDoUpdate({ target: playerPortraits.playerId, set: values }).run();
  }

  const c = outcome.character;
  if (c) {
    // Data and its time stay on show when a later refresh fails or finds nothing; only the next try moves.
    const values = c.status === "ok" && c.data && c.known
      ? {
          characterId: c.known.characterId,
          ncServerId: c.known.serverId,
          attributesJson: JSON.stringify(c.data.attributes),
          wingId: c.data.wingId,
          wingSkinId: c.data.wingSkinId,
          activePet: c.data.activePet,
          activePetLevel: c.data.activePetLevel,
          status: "ok",
          fetchedAt: now,
          nextTryAt: now + CHARACTER_RETRY_AFTER.ok,
        }
      : {
          characterId: c.known?.characterId ?? characterBefore?.characterId ?? null,
          ncServerId: c.known?.serverId ?? characterBefore?.ncServerId ?? null,
          attributesJson: characterBefore?.attributesJson ?? "{}",
          wingId: characterBefore?.wingId ?? null,
          wingSkinId: characterBefore?.wingSkinId ?? null,
          activePet: characterBefore?.activePet ?? null,
          activePetLevel: characterBefore?.activePetLevel ?? null,
          status: c.status,
          fetchedAt: characterBefore?.fetchedAt ?? now,
          nextTryAt: now + CHARACTER_RETRY_AFTER[c.status],
        };
    db.insert(playerNcCharacter).values({ playerId: key.playerId, ...values }).onConflictDoUpdate({ target: playerNcCharacter.playerId, set: values }).run();
  }
}

export const portraitQueue = new PortraitQueue((key) => processPlayer(key));

/**
 * The portrait URL for a player page, or null while there is no picture. Queues a fetch when the portrait or the character data
 * has no valid entry (never fetches in the request). A stale 'ok' keeps showing its old data while the new one is fetched.
 */
export function portraitUrlFor(key: PlayerKey, now = Date.now(), queue: PortraitQueue = portraitQueue): string | null {
  const row = db.select().from(playerPortraits).where(eq(playerPortraits.playerId, key.playerId)).get();
  const characterRow = db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, key.playerId)).get();
  if ((!isFresh(row, now) || !isFresh(characterRow, now)) && process.env.PORTRAITS_OFF !== "1") {
    queue.enqueue(key);
  }
  return row?.status === "ok" ? `/api/players/${key.playerId}/portrait?v=${row.fetchedAt}` : null;
}
