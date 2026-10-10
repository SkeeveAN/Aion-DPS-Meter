import { mkdirSync, renameSync, writeFileSync } from "node:fs";
import path from "node:path";
import { eq } from "drizzle-orm";
import { db } from "./db/client.js";
import { playerPortraits } from "./db/schema.js";
import { portraitFromJpeg } from "./portrait.js";

// Official character portrait of a player from NC's public character search and profile image, shown on the player page.
// Be polite to NC: only profiles somebody opens, one request at a time with a pause between them, results are cached for days.

type FetchFn = typeof fetch;

export const PORTRAIT_DIR = path.resolve(process.env.PORTRAIT_DIR ?? "./data/portraits");

const SEARCH_URL = "https://api-search.plaync.com/aion2global/search/v2/character";
const IMAGE_URL = "https://profileimg.plaync.com/game_profile_images/aion2global/images";
const BROWSER_HEADERS = {
  "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
  Origin: "https://aion2.plaync.com",
  Referer: "https://aion2.plaync.com/",
};
const TIMEOUT_MS = 10_000;
const MAX_IMAGE_BYTES = 2 * 1024 * 1024;
const MAX_SEARCH_BYTES = 256 * 1024;

export const HOUR = 3_600_000;
export const DAY = 24 * HOUR;
/** When to try again, by outcome: a picture can change with the character's looks, "not found" may be a typo or a new character, errors are transient. */
export const RETRY_AFTER = { ok: 7 * DAY, none: 2 * DAY, error: 1 * HOUR } as const;
export type PortraitStatus = keyof typeof RETRY_AFTER;

export type PlayerKey = { playerId: number; name: string; faction: "Elyos" | "Asmodian" | null; ownServerId: number | null };
export type Character = { charKey: string; serverId: number };

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
async function getNc(fetchFn: FetchFn, start: URL, limit: number): Promise<{ res: Response; body: Buffer }> {
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
    found.push({ charKey, serverId });
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

/** One complete attempt for a player: search, download, cut out. Never throws; failures become status 'error'. */
export async function fetchPortrait(fetchFn: FetchFn, key: PlayerKey): Promise<Outcome> {
  try {
    const character = await searchCharacter(fetchFn, key);
    if (!character) {
      return { status: "none", character: null, png: null };
    }
    const png = portraitFromJpeg(await downloadPortrait(fetchFn, character));
    return { status: "ok", character, png };
  } catch {
    return { status: "error", character: null, png: null };
  }
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

/** Fetches for real, writes the file and records the status. */
async function processKey(key: PlayerKey): Promise<void> {
  const now = Date.now();
  const outcome = await fetchPortrait(fetch, key);
  if (outcome.status === "ok" && outcome.png) {
    mkdirSync(PORTRAIT_DIR, { recursive: true });
    const target = portraitFile(key.playerId);
    writeFileSync(`${target}.tmp`, outcome.png);
    renameSync(`${target}.tmp`, target);
  }
  const before = db.select().from(playerPortraits).where(eq(playerPortraits.playerId, key.playerId)).get();
  // A failed refresh keeps the old picture on show and only waits for the next try.
  const values = before?.status === "ok" && outcome.status === "error"
    ? { ...before, nextTryAt: now + RETRY_AFTER.error }
    : {
        charKey: outcome.character?.charKey ?? null,
        ncServerId: outcome.character?.serverId ?? null,
        status: outcome.status,
        fetchedAt: now,
        nextTryAt: now + RETRY_AFTER[outcome.status],
      };
  db.insert(playerPortraits).values({ playerId: key.playerId, ...values }).onConflictDoUpdate({ target: playerPortraits.playerId, set: values }).run();
}

export const portraitQueue = new PortraitQueue(processKey);

/**
 * The portrait URL for a player page, or null while there is no picture. Queues a fetch when there is no valid entry
 * (never fetches in the request). A stale 'ok' keeps showing its old picture while the new one is fetched.
 */
export function portraitUrlFor(key: PlayerKey, now = Date.now(), queue: PortraitQueue = portraitQueue): string | null {
  const row = db.select().from(playerPortraits).where(eq(playerPortraits.playerId, key.playerId)).get();
  if (!isFresh(row, now) && process.env.PORTRAITS_OFF !== "1") {
    queue.enqueue(key);
  }
  return row?.status === "ok" ? `/api/players/${key.playerId}/portrait?v=${row.fetchedAt}` : null;
}
