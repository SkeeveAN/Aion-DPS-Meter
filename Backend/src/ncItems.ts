import { eq } from "drizzle-orm";
import { db } from "./db/client.js";
import { playerNcCharacter, playerNcMatch, playerProfiles } from "./db/schema.js";
import { getNc, ncFetch, withinBudget, DAY, HOUR } from "./portraitService.js";
import { PRIORITY } from "./refreshPolicy.js";

// The item tooltip of the player page, filled from NC's own item pages: what a worn piece really is (base stats with the enchant
// bonus, rolled stats with their range, stone values, the god stone's text, where the piece drops). Asked for only when somebody
// hovers a piece (never in the background), and cached, so NC sees one request per piece and language.

type FetchFn = typeof fetch;

const EQUIPPED_URL = "https://aion2.plaync.com/api/character/equipment/item";
const DEFINITION_HOST = "https://aion2.plaync.com";
const MAX_ITEM_BYTES = 128 * 1024;

/** The site's languages NC's item pages answer in (Global: en, de, es, fr, ja, pt-BR); every other language reads English. */
const NC_LANG: Record<string, string> = { en: "en-US", de: "de-DE", es: "es-ES", fr: "fr-FR", ja: "ja-JP", pt: "pt-BR" };
export function ncLangOf(locale: unknown): string {
  return NC_LANG[typeof locale === "string" ? locale.toLowerCase().split("-")[0] ?? "" : ""] ?? "en-US";
}

export type NcItemStat = { name: string; value: string; min: string | null; max: string | null; bonus: string | null };
export type NcItemTip = {
  itemId: number;
  name: string;
  gradeName: string;
  categoryName: string | null;
  classNames: string[];
  level: number | null;
  /** The "(+n)" the game writes behind the item level; null when a stone's rarity is not one whose worth is known. */
  levelBonus: number | null;
  equipLevel: number | null;
  /** 0..100, null when NC does not say. */
  soulBind: number | null;
  tradable: boolean | null;
  mainStats: NcItemStat[];
  rolled: NcItemStat[];
  skills: { name: string; level: number }[];
  stones: { name: string; value: string; grade: string }[];
  godStones: { name: string; desc: string; grade: string }[];
  sources: string[];
};

type Json = Record<string, unknown>;
const obj = (v: unknown): Json | null => (v !== null && typeof v === "object" && !Array.isArray(v) ? (v as Json) : null);
const list = (v: unknown): Json[] => (Array.isArray(v) ? v.map(obj).filter((x): x is Json => x !== null) : []);
/** NC text is shown as plain text only; length is capped so a strange answer cannot fill the page. */
const text = (v: unknown, max = 400): string => (typeof v === "string" ? v.replace(/<[^>]*>/g, "").trim().slice(0, max) : typeof v === "number" ? String(v) : "");
const num = (v: unknown): number | null => {
  const n = typeof v === "number" ? v : typeof v === "string" && v.trim() !== "" ? Number(v) : NaN;
  return Number.isFinite(n) ? n : null;
};
const strOrNull = (v: unknown): string | null => {
  const s = text(v, 40);
  return s === "" ? null : s;
};

/** What the game adds to the item level (the "(+n)" behind it), measured on 15 pieces: the enchant, each mana stone by rarity (white 1, green 2, blue 3), a god stone 3, every step over +15 five. */
const STONE_POINTS: Record<string, number> = { Common: 1, Rare: 2, Legend: 3 };
const GODSTONE_POINTS: Record<string, number> = { Rare: 3 };
const EXCEED_POINTS = 5;

export function levelBonusOf(enchant: number, exceed: number, stoneGrades: string[], godStoneGrades: string[]): number | null {
  let sum = enchant + EXCEED_POINTS * exceed;
  for (const grade of stoneGrades) {
    const points = STONE_POINTS[grade];
    if (points === undefined) {
      return null;
    }
    sum += points;
  }
  for (const grade of godStoneGrades) {
    const points = GODSTONE_POINTS[grade];
    if (points === undefined) {
      return null;
    }
    sum += points;
  }
  return sum;
}

/**
 * One worn piece (NC's equipment/item answer) merged with the item's own definition (gameconst/item: the range each rolled stat
 * can have). Anything that is not what it should be is left out, never guessed.
 */
export function mapNcItem(equipped: unknown, definition: unknown, exceed = 0): NcItemTip | null {
  const e = obj(equipped);
  if (!e) {
    return null;
  }
  const itemId = num(e.id);
  const name = text(e.name, 120);
  if (itemId === null || name === "") {
    return null;
  }
  const ranges = new Map<string, { min: string | null; max: string | null }>();
  for (const s of list(obj(definition)?.subStats)) {
    ranges.set(text(s.id, 40), { min: strOrNull(s.minValue), max: strOrNull(s.value) });
  }
  const mainStats: NcItemStat[] = list(e.mainStats).map((s) => ({
    name: text(s.name, 60),
    value: text(s.value, 20),
    min: strOrNull(s.minValue),
    max: null,
    bonus: num(s.extra) !== null && num(s.extra) !== 0 ? text(s.extra, 20) : null,
  })).filter((s) => s.name !== "" && s.value !== "");
  const rolled: NcItemStat[] = list(e.subStats).map((s) => {
    const range = ranges.get(text(s.id, 40));
    return { name: text(s.name, 60), value: text(s.value, 20), min: range?.min ?? null, max: range?.max ?? null, bonus: null };
  }).filter((s) => s.name !== "" && s.value !== "");
  const stones = list(e.magicStoneStat).map((s) => ({ name: text(s.name, 60), value: text(s.value, 20), grade: text(s.grade, 20) })).filter((s) => s.name !== "");
  const godStones = list(e.godStoneStat).map((s) => ({ name: text(s.name, 80), desc: text(s.desc, 500), grade: text(s.grade, 20) })).filter((s) => s.name !== "");
  const enchant = num(e.enchantLevel);
  const soulBind = num(e.soulBindRate);
  const tradable = typeof e.tradable === "boolean" ? e.tradable : null;
  return {
    itemId,
    name,
    gradeName: text(e.gradeName, 40),
    categoryName: strOrNull(e.categoryName),
    classNames: Array.isArray(e.classNames) ? e.classNames.map((c) => text(c, 40)).filter((c) => c !== "").slice(0, 8) : [],
    level: num(e.level),
    levelBonus: enchant === null ? null : levelBonusOf(Math.max(0, enchant), Math.max(0, exceed), stones.map((x) => x.grade), godStones.map((x) => x.grade)),
    equipLevel: num(e.equipLevel),
    soulBind: soulBind !== null && soulBind >= 0 && soulBind <= 100 ? soulBind : null,
    tradable,
    mainStats,
    rolled,
    skills: list(e.subSkills).map((s) => ({ name: text(s.name, 80), level: num(s.level) ?? 0 })).filter((s) => s.name !== ""),
    stones,
    godStones,
    sources: Array.isArray(e.sources) ? e.sources.map((s) => text(s, 60)).filter((s) => s !== "").slice(0, 12) : [],
  };
}

// --- caches (memory only: after a restart the first hover asks NC again) ---

type Cached<T> = { at: number; value: T };
const CACHE_LIMIT = 4000;
const tipCache = new Map<string, Cached<NcItemTip | null>>();
const definitionCache = new Map<string, Cached<unknown>>();
const inFlight = new Map<string, Promise<unknown>>();
/** A worn piece changes when the player re-enchants or swaps; six hours keep NC's load low and the page reasonably current. */
export const TIP_TTL = 6 * HOUR;
/** A definition only changes with a game patch. */
export const DEFINITION_TTL = 14 * DAY;

function put<T>(cache: Map<string, Cached<T>>, key: string, value: T, now: number): void {
  if (cache.size >= CACHE_LIMIT) {
    const oldest = cache.keys().next().value;
    if (oldest !== undefined) {
      cache.delete(oldest);
    }
  }
  cache.set(key, { at: now, value });
}

function once<T>(key: string, run: () => Promise<T>): Promise<T> {
  const running = inFlight.get(key) as Promise<T> | undefined;
  if (running) {
    return running;
  }
  const promise = run().finally(() => inFlight.delete(key));
  inFlight.set(key, promise);
  return promise;
}

async function getJson(fetchFn: FetchFn, url: URL): Promise<unknown> {
  const { body } = await getNc(fetchFn, url, MAX_ITEM_BYTES);
  return JSON.parse(body.toString("utf8"));
}

async function definitionOf(fetchFn: FetchFn, itemId: number, lang: string, now: number): Promise<unknown> {
  const key = `${itemId}:${lang}`;
  const hit = definitionCache.get(key);
  if (hit && now - hit.at < DEFINITION_TTL) {
    return hit.value;
  }
  return once(`def:${key}`, async () => {
    const url = new URL(`${DEFINITION_HOST}/${lang.toLowerCase()}/api/gameconst/item`);
    url.searchParams.set("lang", lang);
    url.searchParams.set("id", String(itemId));
    url.searchParams.set("enchantLevel", "0");
    const value = await getJson(fetchFn, url);
    put(definitionCache, key, value, now);
    return value;
  });
}

export type GearEntry = { slot: number; itemId: number; enchant: number; exceed: number };

/** The worn piece of a slot from a stored profile; the enchant byte 16..20 is +15 with Zenit stages, NC's own enchantLevel stops at 15. */
export function gearOfSlot(gearJson: string, slot: number): GearEntry | null {
  let gear: unknown;
  try {
    gear = JSON.parse(gearJson);
  } catch {
    return null;
  }
  const hit = list(gear).find((g) => num(g.slot) === slot);
  const itemId = num(hit?.itemId);
  if (!hit || itemId === null || itemId <= 0) {
    return null;
  }
  const total = Math.max(0, num(hit.enchant) ?? 0);
  return { slot, itemId, enchant: Math.min(15, total), exceed: Math.max(0, total - 15) };
}

export type TipOutcome = { status: "ok"; tip: NcItemTip } | { status: "none" } | { status: "busy" };

/**
 * The tooltip data of one worn piece. Only pieces the stored profile really lists are asked for (the player page cannot be used to
 * ask NC about anything else), only for players NC knows, and only while the hourly budget allows it.
 */
export async function itemTipOf(playerId: number, slot: number, lang: string, fetchFn: FetchFn = ncFetch, now = Date.now()): Promise<TipOutcome> {
  const profile = db.select({ gearJson: playerProfiles.gearJson }).from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get();
  const gear = profile ? gearOfSlot(profile.gearJson, slot) : null;
  if (!gear) {
    return { status: "none" };
  }
  const own = db.select({ characterId: playerNcCharacter.characterId, serverId: playerNcCharacter.ncServerId }).from(playerNcCharacter).where(eq(playerNcCharacter.playerId, playerId)).get();
  const matched = db.select({ characterId: playerNcMatch.ncCharacterId, serverId: playerNcMatch.ncServerId }).from(playerNcMatch).where(eq(playerNcMatch.playerId, playerId)).get();
  const known = own?.characterId && own.serverId ? { characterId: own.characterId, serverId: own.serverId } : matched?.characterId && matched.serverId ? { characterId: matched.characterId, serverId: matched.serverId } : null;
  if (!known) {
    return { status: "none" };
  }
  const key = `${known.characterId}:${gear.slot}:${gear.itemId}:${gear.enchant}:${gear.exceed}:${lang}`;
  const hit = tipCache.get(key);
  if (hit && now - hit.at < TIP_TTL) {
    return hit.value ? { status: "ok", tip: hit.value } : { status: "none" };
  }
  if (!withinBudget(PRIORITY.view, now)) {
    return { status: "busy" };
  }
  try {
    const tip = await once(`tip:${key}`, async () => {
      const url = new URL(EQUIPPED_URL);
      url.searchParams.set("region", "eu");
      url.searchParams.set("lang", lang);
      url.searchParams.set("characterId", known.characterId);
      url.searchParams.set("serverId", String(known.serverId));
      url.searchParams.set("id", String(gear.itemId));
      url.searchParams.set("enchantLevel", String(gear.enchant));
      url.searchParams.set("slotPos", String(gear.slot));
      const equipped = await getJson(fetchFn, url);
      // The definition only adds the stat ranges: without it the tooltip is still right, so a failure there is not an error.
      const definition = await definitionOf(fetchFn, gear.itemId, lang, now).catch(() => null);
      const mapped = mapNcItem(equipped, definition, gear.exceed);
      put(tipCache, key, mapped, now);
      return mapped;
    });
    return tip ? { status: "ok", tip } : { status: "none" };
  } catch {
    return { status: "busy" };
  }
}

/** Test hook: forget everything cached. */
export function clearItemCaches(): void {
  tipCache.clear();
  definitionCache.clear();
  inFlight.clear();
}
