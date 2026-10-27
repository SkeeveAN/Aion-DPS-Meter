import { readFileSync } from "node:fs";
import { desc, and, eq, ne } from "drizzle-orm";
import { z } from "zod";
import { AION2_CLASS_BY_ID } from "./constants.js";
import { db } from "./db/client.js";
import { encounterParticipants, playerNcCharacter, playerProfiles } from "./db/schema.js";
import type { NcCharacterData } from "./ncCharacter.js";

/**
 * Character profile of an Aion 2 player, as an uploading client read it from the game's traffic.
 * Ids only: item names, item levels, skill names and Daevanion effects are resolved from the game
 * data when the profile is read (see buildProfileView), so correcting that data needs no migration.
 *
 * "self" is the uploader's own character (level, full equipment with enchants, skills, Daevanion);
 * "seen" is what a client could read off another player (class, faction and the visible equipment).
 */
const skillSchema = z.object({
  id: z.number().int().min(1).max(2_000_000_000),
  level: z.number().int().min(0).max(60),
  baseLevel: z.number().int().min(0).max(60).default(0),
  // Stigmas of the uploader's own character (the skills the game lists with five variants).
  stigma: z.boolean().default(false),
  // On the first macro page of the uploader's skill bar (what the game's skill window lists as equipped).
  equipped: z.boolean().default(false),
  // The choice in each specialisation slot (1-5 = the skill's five stones, 0 = empty); absent when the client did not read it.
  stones: z.array(z.number().int().min(0).max(9)).max(5).optional(),
});

export const profileSchema = z.object({
  source: z.enum(["self", "seen"]),
  level: z.number().int().min(1).max(200).optional(),
  classId: z.number().int().min(1).max(9).optional(),
  // The last part of the class code (4 * class id + 1..4): 1 and 2 on Elyos characters, 3 on Asmodian ones (4 expected). Not a faction. Clients up to 0.27 sent them as "faction";
  // 1.5.9 sent the raw remainder (0 to 3) for Asmodians, so 0 is accepted too.
  classBits: z.number().int().min(0).max(4).optional(),
  faction: z.number().int().min(1).max(2).optional(),
  // The gear score ("Ausrüstungswert") the game shows in a character window (only when the uploader opened that window).
  gearScore: z.number().int().min(1).max(100_000_000).optional(),
  gear: z
    .array(
      z.object({
        slot: z.number().int().min(0).max(255),
        itemId: z.number().int().min(1).max(2_000_000_000),
        enchant: z.number().int().min(0).max(30).default(0),
        // The mana stone slots of the piece: the stat the stone gives (0 = empty slot) and its tier (1 white, 2 green, 3 blue).
        // The amount a stone adds is not part of the game's data frame. Optional: older clients send neither field.
        stones: z.array(z.object({ stat: z.number().int().min(0).max(100_000), tier: z.number().int().min(0).max(9) })).max(8).optional(),
        // Stats rolled on the piece itself (stat id and value in the game's units; percent stats in hundredths).
        stats: z.array(z.object({ stat: z.number().int().min(0).max(100_000), value: z.number().int().min(0).max(4_000_000_000) })).max(12).optional(),
        // The godstone set into the piece (its item id) and the skill level bonuses the piece gives.
        godstone: z.number().int().min(1).max(2_000_000_000).optional(),
        skillBonuses: z.array(z.object({ skill: z.number().int().min(1).max(2_000_000_000), level: z.number().int().min(1).max(20) })).max(8).optional(),
        // An arcana piece of the own character (login list): the experience collected towards the next level, the god stat (id 7-11 = Nezekan,
        // Vaizel, Kaisinel, Yustiel, Siel) with its value, and the skill levels the piece gives (own and rolled levels added up).
        arcana: z
          .object({
            exp: z.number().int().min(0).max(100_000_000),
            god: z.number().int().min(0).max(100),
            godValue: z.number().int().min(0).max(100_000),
            skillBonuses: z.array(z.object({ skill: z.number().int().min(1).max(2_000_000_000), level: z.number().int().min(1).max(40) })).max(8),
          })
          .optional(),
      }),
    )
    // 25 pieces seen on a real character (slots up to 29); the cap only fends off nonsense.
    .max(64)
    .default([]),
  skills: z.array(skillSchema).max(400).default([]),
  // Own character only: every skill preset seen so far, the active one marked (skills above is the active preset).
  presets: z
    .array(
      z.object({
        index: z.number().int().min(1).max(9),
        active: z.boolean().default(false),
        seenAt: z.string().max(40).optional(),
        skills: z.array(skillSchema).max(400).default([]),
      }),
    )
    .max(9)
    .default([]),
  daevanion: z
    .array(
      z.object({
        board: z.number().int().min(1).max(10_000),
        nodes: z.array(z.number().int().min(1).max(2_000_000_000)).max(400),
      }),
    )
    .max(12)
    .default([]),
  // The pet window's species knowledge (own character): id 2 Cognia, 3 Fera, 4 Natura, 5 Varia, 6 Specia.
  species: z
    .array(
      z.object({
        id: z.number().int().min(1).max(20),
        level: z.number().int().min(0).max(99),
        progress: z.number().int().min(0).max(1_000_000_000),
        effects: z
          .array(
            z.object({
              page: z.number().int().min(1).max(5),
              slot: z.number().int().min(0).max(32),
              stat: z.number().int().min(1).max(100_000),
              value: z.number().int().min(-1_000_000_000).max(1_000_000_000),
              // Quality of the slot (1 white, 2 green, 3 blue, 4 gold, 5 orange); 0 when the client did not send it.
              kind: z.number().int().min(0).max(9).default(0),
            }),
          )
          .max(100)
          .default([]),
      }),
    )
    .max(10)
    .default([]),
  // The worn titles (slot 1..3 and the game's title id).
  titles: z
    .array(z.object({ slot: z.number().int().min(1).max(3), titleId: z.number().int().min(1).max(2_000_000_000) }))
    .max(3)
    .default([]),
  // The pet circles: species 2 Cognia .. 6 Specia, its level and the quality of each effect slot (0 empty, 1 white .. 5 orange).
  pets: z
    .array(
      z.object({
        species: z.number().int().min(1).max(20),
        level: z.number().int().min(0).max(99),
        kinds: z.array(z.number().int().min(0).max(9)).max(32).default([]),
      }),
    )
    .max(10)
    .default([]),
  // Own character only: the ranks the game's ranking window showed (type 1 Abyss, 3 Nightmare, 4 Transcendence, 5 Arena 1v1, 6 Arena 5v5, 21 Ascension rite; scope 0 overall, 2 within the class).
  ranks: z
    .array(
      z.object({
        type: z.number().int().min(1).max(99),
        scope: z.number().int().min(0).max(9),
        rank: z.number().int().min(1).max(10_000_000),
        points: z.number().int().min(0).max(1_000_000_000_000),
        change: z.number().int().min(-1_000_000).max(1_000_000).default(0),
        grade: z.number().int().min(0).max(1000).default(0),
        // The stats the game lists beside the rank, as its own JSON text (kills, deaths, wins ...); parsed when shown.
        stats: z.string().max(300).optional(),
        at: z.string().max(40),
      }),
    )
    .max(24)
    .default([]),
  // Own character only: the Pantheon. Slots 1-12 paintings, 13-16 statues, 17 the colossus; id is the game's decoration number (paintings: the
  // item number's last digits, statues 1000 + n - 1, colossi 5000 + n - 1). presets are the setups seen (the login sends the active one); owned are
  // all registered decorations.
  pantheon: z
    .object({
      presets: z
        .array(
          z.object({
            preset: z.number().int().min(1).max(3),
            slots: z.array(z.object({ slot: z.number().int().min(1).max(17), id: z.number().int().min(1).max(100_000) })).max(17),
          }),
        )
        .max(3)
        .default([]),
      owned: z.array(z.number().int().min(1).max(100_000)).max(400).default([]),
    })
    .optional(),
  // Another player's activated node count per Daevanion board (their node lists are not sent).
  boardCounts: z
    .array(z.object({ board: z.number().int().min(1).max(10_000), count: z.number().int().min(0).max(1000) }))
    .max(12)
    .default([]),
  // Own character only (optional, older clients omit them): attribute id ("1".."17") -> value, the worn wing and wing skin
  // (item ids; 0 = none) and the active pet's species id (0 = none).
  attributes: z.record(z.string().regex(/^\d{1,3}$/), z.number().int().min(-1_000_000).max(1_000_000)).optional(),
  wingId: z.number().int().min(0).max(2_000_000_000).optional(),
  wingSkinId: z.number().int().min(0).max(2_000_000_000).optional(),
  activePet: z.number().int().min(0).max(2_000_000_000).optional(),
  activePetLevel: z.number().int().min(0).max(1000).optional(),
  // Own character only: a hash (16 hex digits) of the account key the game sends with the login's character list; the same on every character of one account.
  accountKey: z.string().regex(/^[0-9a-f]{16}$/).optional(),
});

export type ProfileUpload = z.infer<typeof profileSchema>;

type RankReading = { type: number; scope: number; at: string; [key: string]: unknown };

type PantheonData = { presets: { preset: number; slots: { slot: number; id: number }[] }[]; owned: number[] };

/** The stored Pantheon with a fresh reading: a setup replaces the stored setup of its preset (an empty one clears it), the others stay; owned decorations are replaced when sent. */
export function mergePantheon(storedJson: string | undefined, fresh: Partial<PantheonData>): PantheonData {
  let stored: Partial<PantheonData> = {};
  try {
    stored = storedJson ? (JSON.parse(storedJson) as Partial<PantheonData>) : {};
  } catch {
    stored = {};
  }

  const presets = new Map<number, { preset: number; slots: { slot: number; id: number }[] }>();
  for (const p of stored.presets ?? []) presets.set(p.preset, p);
  for (const p of fresh.presets ?? []) presets.set(p.preset, p);
  return {
    presets: [...presets.values()].sort((a, b) => a.preset - b.preset),
    owned: fresh.owned && fresh.owned.length > 0 ? [...fresh.owned].sort((a, b) => a - b) : (stored.owned ?? []),
  };
}

export function mergeRanks(storedJson: string | undefined, fresh: RankReading[]): RankReading[] {
  const byKey = new Map<string, RankReading>();
  for (const r of JSON.parse(storedJson ?? "[]") as RankReading[]) {
    byKey.set(`${r.type}:${r.scope}`, r);
  }
  for (const r of fresh) {
    const known = byKey.get(`${r.type}:${r.scope}`);
    if (!known || known.at <= r.at) {
      byKey.set(`${r.type}:${r.scope}`, r);
    }
  }
  return [...byKey.values()].sort((a, b) => a.type - b.type || a.scope - b.scope);
}

/**
 * Stores a profile for a player. The newest data wins - except that a "seen" profile never replaces
 * a "self" one (the uploader's own record is the richer, authoritative one; a stranger's clients
 * only see part of it), and a "seen" profile that carries nothing (no gear, no class) is dropped.
 */
export function upsertProfile(playerId: number, input: Partial<ProfileUpload> & Pick<ProfileUpload, "source">): void {
  // The upload route has already run profileSchema (which fills these defaults); callers that go
  // straight to the merge code may omit the lists.
  const profile = { ...input, gear: input.gear ?? [], skills: input.skills ?? [], presets: input.presets ?? [], daevanion: input.daevanion ?? [], species: input.species ?? [], titles: input.titles ?? [], pets: input.pets ?? [], boardCounts: input.boardCounts ?? [], ranks: input.ranks ?? [] };
  // A client that carries a leftover of another character (a twink played before) sends boards of the wrong class: a board's
  // id starts with its class (41 = Assassin's first board). Boards of any other class than the profile's are dropped.
  const boardClass = profile.classId ?? (db.select({ classId: playerProfiles.classId }).from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get()?.classId ?? undefined);
  if (boardClass !== undefined) {
    profile.daevanion = profile.daevanion.filter((b) => Math.floor(b.board / 10) === boardClass);
    profile.boardCounts = profile.boardCounts.filter((b) => Math.floor(b.board / 10) === boardClass);
  }
  if (profile.source === "seen" && profile.gear.length === 0 && profile.classId === undefined && profile.level === undefined) {
    return;
  }

  const row = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get();
  if (row?.source === "self" && profile.source === "seen") {
    return;
  }
  // A profile built from NC's page has the whole equipment: a "seen" one replaces it only with as many pieces or more.
  if (row?.source === "nc" && profile.source === "seen" && profile.gear.length < (JSON.parse(row.gearJson) as unknown[]).length) {
    return;
  }
  // Nothing of an NC profile is carried over into a profile from the game: its lists are NC's, not what the client read.
  const existing = row?.source === "nc" ? undefined : row;

  const values = {
    source: profile.source,
    level: profile.level ?? existing?.level ?? null,
    classId: profile.classId ?? existing?.classId ?? null,
    classBits: profile.classBits ?? profile.faction ?? existing?.classBits ?? null,
    gearScore: profile.gearScore ?? existing?.gearScore ?? null,
    gearJson: JSON.stringify(profile.gear),
    // The skill list, the Daevanion boards and the species knowledge only exist for "self"; a later upload from the same
    // person that lacks them (their client started mid-session) must not wipe what was stored.
    skillsJson: profile.skills.length > 0 ? JSON.stringify(profile.skills) : (existing?.skillsJson ?? "[]"),
    // The presets come with the skills: a client that sends skills without presets (an older one) has no others to offer, and the
    // presets stored from a newer client would show a state that is no longer true.
    presetsJson: profile.presets.length > 0 ? JSON.stringify(profile.presets) : profile.skills.length > 0 ? "[]" : (existing?.presetsJson ?? "[]"),
    daevanionJson: profile.daevanion.length > 0 ? JSON.stringify(profile.daevanion) : (existing?.daevanionJson ?? "[]"),
    speciesJson: profile.species.length > 0 ? JSON.stringify(profile.species) : (existing?.speciesJson ?? "[]"),
    // Titles, pets and board counts: an upload that lacks them (older client, a window without that part) keeps what is stored.
    titlesJson: profile.titles.length > 0 ? JSON.stringify(profile.titles) : (existing?.titlesJson ?? "[]"),
    petsJson: profile.pets.length > 0 ? JSON.stringify(profile.pets) : (existing?.petsJson ?? "[]"),
    boardCountsJson: profile.boardCounts.length > 0 ? JSON.stringify(profile.boardCounts) : (existing?.boardCountsJson ?? "[]"),
    // The ranks of the ranking window: a new reading replaces the one of its type and scope, the others stay.
    ranksJson: profile.source === "self" && profile.ranks.length > 0 ? JSON.stringify(mergeRanks(existing?.ranksJson, profile.ranks)) : (existing?.ranksJson ?? "[]"),
    // The Pantheon (own character): a setup replaces the stored one of its preset, the others stay; a list of owned decorations replaces the stored one.
    pantheonJson: profile.source === "self" && profile.pantheon ? JSON.stringify(mergePantheon(existing?.pantheonJson, profile.pantheon)) : (existing?.pantheonJson ?? "{}"),
    // Character window (own character): an upload without attributes, wing or pet (older client, window not read) keeps what is stored.
    attributesJson: profile.attributes && Object.keys(profile.attributes).length > 0 ? JSON.stringify(profile.attributes) : (existing?.attributesJson ?? "{}"),
    wingId: profile.wingId ? profile.wingId : (existing?.wingId ?? null),
    wingSkinId: profile.wingSkinId ? profile.wingSkinId : (existing?.wingSkinId ?? null),
    activePet: profile.activePet ? profile.activePet : (existing?.activePet ?? null),
    // The level belongs to the pet it was read for: a new pet without a level does not inherit the old pet's.
    accountKey: profile.source === "self" && profile.accountKey ? profile.accountKey : (existing?.accountKey ?? null),
    activePetLevel: profile.activePetLevel ? profile.activePetLevel : (!profile.activePet || profile.activePet === existing?.activePet ? (existing?.activePetLevel ?? null) : null),
  };
  if (row) {
    db.update(playerProfiles)
      .set({ ...values, updatedAt: new Date().toISOString().slice(0, 19).replace("T", " ") })
      .where(eq(playerProfiles.playerId, playerId))
      .run();
  } else {
    db.insert(playerProfiles).values({ playerId, ...values }).run();
  }
}

type ProfileSkill = {
  id: number;
  name: string;
  names?: Record<string, string>;
  icon: string | null;
  passive: boolean;
  stigma: boolean;
  equipped: boolean;
  level: number;
  baseLevel: number;
  /** The choice in each specialisation slot of an active skill (1-5 = the skill's five stones, 0 = empty); null when not read. */
  stones: number[] | null;
};
type StoredSkill = { id: number; level: number; baseLevel: number; stigma?: boolean; equipped?: boolean; stones?: number[] };

/** The skill rows as the skill window lists them: only the base entries (the list also carries each skill's specialisation
 *  variants, which would repeat the same name), highest level first. */
function skillRows(list: StoredSkill[]): ProfileSkill[] {
  return list
    .filter((s) => s.id % 10000 === 0)
    .map((s) => ({ id: s.id, name: skillNames![String(s.id)] ?? String(s.id), names: skillNamesI18n![String(s.id)], icon: skillIcons![String(s.id)] ?? null, passive: skillTypes![String(s.id)] === "p", stigma: s.stigma === true, equipped: s.equipped === true, level: s.level, baseLevel: s.baseLevel, stones: s.stones ?? null }))
    .sort((a, b) => b.level - a.level || a.name.localeCompare(b.name));
}

const NODE_GRADE: Record<string, number> = { Common: 1, Rare: 2, Legend: 3, Unique: 4 };
type ItemInfo = [string, string, number, number, number]; // name, slot, grade, tier, item level
type DaevanionData = {
  boards: Record<string, [string, string]>;
  nodes: Record<string, [number, number, number, string, string, string, number]>;
};

function loadJson<T>(file: string, fallback: T): T {
  try {
    return JSON.parse(readFileSync(new URL(`./data/aion2/${file}`, import.meta.url), "utf8")) as T;
  } catch {
    return fallback;
  }
}

let itemInfo: Record<string, ItemInfo> | null = null;
let skillNames: Record<string, string> | null = null;
// Player skills in every language of the game client (de, en, es, fr, ja, ko, pt, ru), by skill id.
let skillNamesI18n: Record<string, Record<string, string>> | null = null;
let daevanion: DaevanionData | null = null;
// Icon file names (without extension) by item id / skill id, extracted from the game client; ids
// without an entry (unreleased items, passives the client table gives no icon for) have none.
let itemIcons: Record<string, string> | null = null;
// English item name -> names in the client languages (Tools/aion2-dat, matched by the English text).
let itemNamesI18n: Record<string, Record<string, string>> | null = null;
let skillIcons: Record<string, string> | null = null;
let skillTypes: Record<string, string> | null = null; // "a" active, "p" passive
type SpeciesData = {
  species: Record<string, { key: string; names: Record<string, string> }>;
  stats: Record<string, { names: Record<string, string>; percent?: boolean }>;
};
let speciesData: SpeciesData | null = null;
// Stat id -> names (Tools/aion2-dat/build_item_stats.py) for the mana stones and rolled stats on equipment.
let itemStats: Record<string, { names: Record<string, string>; percent?: boolean; hundredths?: boolean; stone?: Record<string, number> }> | null = null;
// A mana stone's value depends on the group it was rolled from, which a piece does not tell. The groups of the low pieces agree with each other and fit every
// piece up to this item level that players showed (Tools/aion2-dat/build_item_stats.py); above it the groups differ, so no value is shown.
const LOW_STONE_LEVEL = 76;
// Godstone item id -> names in the client languages (Tools/aion2-dat/build_item_stats.py).
let godstones: Record<string, Record<string, string>> | null = null;
// Title id -> names in every client language and the game's grade (Tools/aion2-dat/build_titles.py).
let titleData: Record<string, { n: Record<string, string>; g: string | null }> | null = null;
let boardNodes: Map<number, { id: number; node: DaevanionData["nodes"][string] }[]> | null = null;

// The match report only stores skill names, not ids. A name is mapped back to ids to find the icon and
// to tell a player's own skills from effects: the game names an effect of a skill separately
// ("Predation" is an effect of Lifestealing Blade, id 11340027), and a few entries are not damage at all.
const CLASS_BY_SKILL_PREFIX = ["Gladiator", "Templar", "Assassin", "Ranger", "Sorcerer", "Spiritmaster", "Cleric", "Chanter", "Brawler"];
let skillIdsByName: Map<string, number[]> | null = null;

function classOfSkillId(id: number): string | null {
  const prefix = Math.floor(id / 1_000_000);
  return id >= 10_000_000 && prefix >= 11 && prefix <= 19 ? CLASS_BY_SKILL_PREFIX[prefix - 11] : null;
}

export function skillIconByName(name: string, className?: string): string | null {
  const ids = skillIdsOf(name);
  const icons = skillIcons ?? loadJson<Record<string, string>>("skill_icons.json", {});
  skillIcons = icons;
  const own = className ? ids.filter((id) => classOfSkillId(id) === className) : [];
  const ranks = own.filter((id) => id % 10 === 0);
  const first = ranks.length > 0 ? ranks : own;
  for (const id of own.length > 0 ? [...first.map((i) => Math.floor(i / 10000) * 10000), ...first] : ids) {
    if (icons[String(id)]) {
      return icons[String(id)];
    }
  }
  return null;
}

/** The skill's name in every language the game client ships (de, en, es, fr, ja, ko, pt, ru), if known. */
export function skillNamesByName(name: string, className?: string): Record<string, string> | undefined {
  skillNamesI18n ??= loadJson<Record<string, Record<string, string>>>("skill_names_i18n.json", {});
  const ids = skillIdsOf(name);
  const own = className ? ids.filter((id) => classOfSkillId(id) === className) : [];
  const ranks = own.filter((id) => id % 10 === 0);
  for (const id of [...ranks, ...own, ...ids]) {
    const names = skillNamesI18n[String(id)];
    if (names) {
      return names;
    }
  }
  return undefined;
}

/** The English name of a skill id of the client's table (a skill has one id per variant and level step), or null. */
export function skillNameById(id: number): string | null {
  skillNames ??= loadJson<Record<string, string>>("skill_names.json", {});
  return skillNames[String(id)] ?? null;
}

/** Chain skills of the client: skill id -> [follow-up skill id, window in ms] (Tools/aion2-dat/build_skill_chains.py). */
export function skillChains(): Record<string, [number, number][]> {
  skillChainTable ??= loadJson<Record<string, [number, number][]>>("skill_chains.json", {});
  return skillChainTable;
}
let skillChainTable: Record<string, [number, number][]> | null = null;

/** Name, names in every client language and icon of one skill by id, for skills the player did not use at all (a missed chain). */
export function skillInfoById(id: number): { name: string; names?: Record<string, string>; icon: string | null } | null {
  skillNames ??= loadJson<Record<string, string>>("skill_names.json", {});
  skillNamesI18n ??= loadJson<Record<string, Record<string, string>>>("skill_names_i18n.json", {});
  skillIcons ??= loadJson<Record<string, string>>("skill_icons.json", {});
  const name = skillNames[String(id)];
  if (!name) {
    return null;
  }
  return { name, names: skillNamesI18n[String(id)], icon: skillIcons[String(id)] ?? skillIcons[String(Math.floor(id / 10000) * 10000)] ?? null };
}

function skillIdsOf(name: string): number[] {
  if (!skillIdsByName) {
    skillIdsByName = new Map();
    for (const [id, n] of Object.entries(loadJson<Record<string, string>>("skill_names.json", {}))) {
      skillIdsByName.set(n, [...(skillIdsByName.get(n) ?? []), Number(id)]);
    }
  }
  return skillIdsByName.get(name) ?? [];
}

type ReportSkill = { skillName: string; hits: number; critHits: number; totalDamage: number; minHit: number; maxHit: number; isHeal: boolean };

/**
 * A player's skill rows as the skill window would list them: the effect of a skill of the player's own
 * class is added to that skill; a skill that only exists for another class and a "hit" of exactly 1
 * damage per hit (a status marker, not damage) are left out.
 */
export function ownSkillRows<T extends ReportSkill>(className: string, rows: T[]): T[] {
  // The shared once-loaded copy: the 500 KB file was parsed again on every call, per participant of every report.
  skillNames ??= loadJson<Record<string, string>>("skill_names.json", {});
  const names = skillNames;
  const merged = new Map<string, T>();
  for (const row of rows) {
    const ids = skillIdsOf(row.skillName);
    const own = ids.filter((id) => classOfSkillId(id) === className);
    if ((!row.isHeal && row.totalDamage <= row.hits) || (ids.length > 0 && own.length === 0 && ids.every((id) => classOfSkillId(id) !== null))) {
      continue;
    }
    // A rank id ends in 0 (11340010); an id ending otherwise (11340027) is an effect of the skill in the same family.
    const isEffectOnly = own.length > 0 && own.every((id) => id % 10 !== 0);
    const parentName = isEffectOnly ? (names[String(Math.floor(Math.min(...own) / 10000) * 10000)] ?? row.skillName) : row.skillName;
    const into = merged.get(parentName);
    if (!into) {
      merged.set(parentName, { ...row, skillName: parentName });
    } else {
      into.hits += row.hits;
      into.critHits += row.critHits;
      into.totalDamage += row.totalDamage;
      into.minHit = Math.min(into.minHit, row.minHit);
      into.maxHit = Math.max(into.maxHit, row.maxHit);
    }
  }
  return [...merged.values()].sort((x, y) => y.totalDamage - x.totalDamage);
}

export type SpeciesView = {
  id: number;
  key: string;
  names: Record<string, string>;
  level: number;
  progress: number;
  effects: { page: number; slot: number; stat: number; name: string; names?: Record<string, string>; value: number; percent: boolean; kind: number }[];
};

export function factionFromFights(playerId: number): "Elyos" | "Asmodian" | null {
  const row = db
    .select({ faction: encounterParticipants.faction })
    .from(encounterParticipants)
    .where(and(eq(encounterParticipants.playerId, playerId), ne(encounterParticipants.faction, "")))
    .orderBy(desc(encounterParticipants.id))
    .get();
  return row?.faction === "Elyos" || row?.faction === "Asmodian" ? row.faction : null;
}

export type ProfileView = {
  source: "self" | "seen" | "nc";
  updatedAt: string;
  level: number | null;
  className: string | null;
  faction: "Elyos" | "Asmodian" | null;
  gear: {
    slot: number;
    slotName: string;
    itemId: number;
    name: string;
    names?: Record<string, string>;
    icon: string | null;
    itemLevel: number;
    grade: number;
    tier: number;
    /** The raw enchant byte of the piece (15 plus the Zenit stages above +15). */
    enchant: number;
    /** Zenit stage 0..5 = enchant - 15 (the game shows +15 and up to five diamonds; seen 16/18/20 on real pieces); 0 for anything else, including implausible values above 20. */
    zenit: number;
    /** Orange extra values of a weapon from its Zenit stage - DERIVED, not read: the amplify window showed stage 1 -> 2 as Attack 10 -> 20 and Damage Boost 1 % -> 2 %, higher stages are extrapolated linearly. */
    zenitBonus: { attack: number; damageBoostPercent: number } | null;
    /** Mana stone slots (stat 0 = empty); null when the uploader's client did not read them. value is the amount the stone adds
     *  where it was seen in a tooltip for that stat and tier, otherwise null (the frame does not carry it). */
    stones: { stat: number; name: string; names?: Record<string, string>; tier: number; value: number | null }[] | null;
    /** Stats rolled on the piece; percent values are in hundredths. Null when not read. */
    stats: { stat: number; name: string; names?: Record<string, string>; value: number; percent: boolean }[] | null;
    /** The godstone set into the piece (null = none or not read). */
    godstone: { itemId: number; name: string; names?: Record<string, string> } | null;
    /** Skill level bonuses of the piece ("Abwärtsschlag +1"). */
    skillBonuses: { skill: number; name: string; names?: Record<string, string>; level: number }[] | null;
    /** Arcana pieces of the own character: experience towards the next level, god stat id and value, skill levels of the piece; null when not read. */
    arcana: { exp: number; god: number; godValue: number; skillBonuses: { skill: number; name: string; names?: Record<string, string>; level: number }[] } | null;
  }[];
  averageItemLevel: number | null;
  skills: ProfileSkill[];
  /** Every skill preset seen so far (own character), the active one marked; empty for a character without presets or an old client. */
  presets: { index: number; active: boolean; seenAt: string | null; skills: ProfileSkill[] }[];
  daevanion: {
    board: number;
    name: string;
    activeNodes: number;
    knownNodes: number;
    /** Points spent / available on the board: a node is worth its grade (white 1, green 2, blue 3, gold 4); the start node is free. */
    activePoints: number;
    totalPoints: number;
    stats: Record<string, number>;
    skillBonuses: { id: number; name: string; names?: Record<string, string>; value: number }[];
    /**
     * Every node of the board map: [row, col, kind, ref, active, grade, value] (1-based; kind 0 start,
     * 1 stat, 2 skill level; ref is the stat token (kind 1) or skill id (kind 2); active 1/0; grade 1
     * common, 2 rare, 3 legend, 4 unique; value is the stat amount or skill levels).
     */
    cells: [number, number, number, string | number, number, number, number][];
  }[];
  /** Species knowledge of the pet window; percent effects carry hundredths (145 = 1.45 %). */
  species: SpeciesView[];
  /** The worn titles (slot 1..3); grade is the game's ETitleGrade (Common, Rare, Epic, Legend, Unique, Special). */
  titles: { slot: number; titleId: number; name: string; names: Record<string, string>; grade: string | null }[];
  /** The own ranks the game's ranking window showed (own character only): type 1 Abyss, 3 Nightmare, 4 Transcendence, 5 Arena 1v1, 6 Arena 5v5, 21 Ascension rite; scope 0 overall, 2 within the class. */
  ranks: { type: number; scope: number; rank: number; points: number; change: number; grade: number; stats: Record<string, number> | null; at: string }[];
  /** The Pantheon (own character only): the setups seen per preset (slot 1-12 paintings, 13-16 statues, 17 colossus; id = the game's decoration number) and the owned decorations; null when never read. */
  pantheon: PantheonView | null;
  /** One circle per species: the quality of each effect slot (0 empty, 1 white, 2 green, 3 blue, 4 gold, 5 orange). */
  pets: { species: number; key: string; names: Record<string, string>; level: number; kinds: number[] }[];
  /** Daevanion board progress: points (node grades) for the own character from the node lists; other players only have a node count. */
  boards: { board: number; name: string; count: number; total: number }[];
  /**
   * Main attributes and lord values of the own character, grouped and in the game's display order (main: Might, Agility,
   * Precision, Will, Intelligence, Constitution; lords: Justice .. Freedom, clockwise from the top); null for strangers / old uploads.
   * `key` is a stable English id for the website's translations; every entry is present (0 when the client sent none).
   */
  attributes: { main: { id: number; key: string; value: number }[]; lords: { id: number; key: string; value: number }[] } | null;
  /** Worn wing / wing skin (item ids resolved through wings.json; unknown ids read "Wings <id>" without an icon) and the active pet. */
  wing: { id: number; name: string; names: Record<string, string>; icon: string | null; grade: string | null } | null;
  wingSkin: { id: number; name: string; names: Record<string, string>; icon: string | null; grade: string | null } | null;
  /** Active pet by species id; `icon` is a file name under images/aion2/icons/pet (may not exist for every pet). */
  activePet: { id: number; name: string; names: Record<string, string>; icon: string | null; level: number | null } | null;
  /** Where `attributes` come from: the player's own client or NC's official character page (null without attributes). The wing and pet fields are merged per field the same way. */
  characterSource: "client" | "nc" | null;
  /** When that data was read: the client's profile update, or the last successful fetch from NC (ISO time). */
  characterFetchedAt: string | null;
  /** Cut-out official portrait (set by the player route, not by buildProfileView; null while there is none). */
  portraitUrl: string | null;
};

// Attribute ids of the character window, in display order.
const MAIN_ATTRIBUTES: [number, string][] = [[1, "str"], [2, "dex"], [5, "agi"], [6, "wis"], [3, "int"], [4, "con"]];
const LORD_ATTRIBUTES: [number, string][] = [[7, "justice"], [13, "destruction"], [14, "death"], [15, "wisdom"], [16, "destiny"], [17, "space"], [11, "time"], [10, "life"], [9, "illusion"], [8, "freedom"]];
// wings.json (Recherche): item id -> { names, icon, grade (client table: Common/Rare/Epic/Legend/Unique/Special) }; petInfo: species id -> { k: key, n: names }.
let wingData: Record<string, { names?: Record<string, string>; icon?: string; grade?: string | null }> | null = null;
let petInfo: Record<string, { k: string; n: Record<string, string> }> | null = null;

/** Zenit stage of an enchant byte: 1..5 for 16..20, else 0 (+15 is the plain enchant cap; above 20 is not plausible and is left as is). */
export function zenitStage(enchant: number): number {
  return enchant > 15 && enchant <= 20 ? enchant - 15 : 0;
}

/** Average item level of the worn pieces whose level is known; null when none is. */
export function averageItemLevelOf(gearJson: string): number | null {
  itemInfo ??= loadJson<Record<string, ItemInfo>>("item_info.json", {});
  const levels = (JSON.parse(gearJson) as { itemId: number }[]).map((g) => itemInfo![String(g.itemId)]?.[4] ?? 0).filter((l) => l > 0);
  return levels.length > 0 ? Math.round((levels.reduce((s, l) => s + l, 0) / levels.length) * 10) / 10 : null;
}

/** What NC's official character page adds to a character: combat power, the website's item level and the legion it files him under. */
export type NcExtras = { combatPower: number | null; itemLevel: number | null; ncGuild: string | null };

export type CharacterView = Pick<ProfileView, "attributes" | "wing" | "wingSkin" | "activePet" | "characterSource" | "characterFetchedAt"> & NcExtras;

type ClientCharacter = { attributesJson: string; wingId: number | null; wingSkinId: number | null; activePet: number | null; activePetLevel: number | null };

/**
 * Character window data of a player from both sources. What the player's own client uploaded (profile source "self") wins
 * per field; whatever it lacks is filled from NC's official character page. Pure, for tests.
 */
export function mergeCharacter(
  client: (ClientCharacter & { source: "self" | "seen" | "nc"; updatedAt: string }) | null | undefined,
  nc: (ClientCharacter & Partial<NcExtras> & { fetchedAt: number }) | null | undefined,
): CharacterView {
  wingData ??= loadJson("wings.json", {});
  petInfo ??= loadJson("pets.json", {});
  const own = client?.source === "self" ? client : null;
  const clientAttributes = own && Object.keys(JSON.parse(own.attributesJson) as object).length > 0 ? own.attributesJson : null;
  const ncAttributes = nc && Object.keys(JSON.parse(nc.attributesJson) as object).length > 0 ? nc.attributesJson : null;
  // The pet and its level belong together: the client's pet wins with its own level.
  const pet = own?.activePet ? own : nc?.activePet ? nc : null;
  return {
    attributes: attributesOf(clientAttributes ?? ncAttributes ?? "{}"),
    wing: wingOf(own?.wingId || nc?.wingId || null),
    wingSkin: wingOf(own?.wingSkinId || nc?.wingSkinId || null),
    activePet: pet ? activePetOf(pet.activePet, pet.activePetLevel) : null,
    characterSource: clientAttributes ? "client" : ncAttributes ? "nc" : null,
    combatPower: nc?.combatPower ?? null,
    itemLevel: nc?.itemLevel ?? null,
    ncGuild: nc?.ncGuild || null,
    characterFetchedAt: clientAttributes ? (own as { updatedAt: string }).updatedAt : ncAttributes ? new Date((nc as { fetchedAt: number }).fetchedAt).toISOString() : null,
  };
}

/** Character data of a player (also those without any uploaded profile, only NC's data); all null when there is none. */
export function buildCharacterView(playerId: number): CharacterView {
  const client = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get();
  const nc = db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, playerId)).get();
  return mergeCharacter(client, nc);
}

/** The stored profile of a player with every id resolved to a name; null when none was uploaded. */
export function buildProfileView(playerId: number): ProfileView | null {
  const row = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get();
  if (!row) {
    return null;
  }
  itemInfo ??= loadJson<Record<string, ItemInfo>>("item_info.json", {});
  skillNames ??= loadJson<Record<string, string>>("skill_names.json", {});
  skillNamesI18n ??= loadJson<Record<string, Record<string, string>>>("skill_names_i18n.json", {});
  daevanion ??= loadJson<DaevanionData>("daevanion_nodes.json", { boards: {}, nodes: {} });
  itemIcons ??= loadJson<Record<string, string>>("item_icons.json", {});
  itemNamesI18n ??= loadJson<Record<string, Record<string, string>>>("item_names_i18n.json", {});
  skillIcons ??= loadJson<Record<string, string>>("skill_icons.json", {});
  skillTypes ??= loadJson<Record<string, string>>("skill_types.json", {});
  speciesData ??= loadJson<SpeciesData>("species_stats.json", { species: {}, stats: {} });
  titleData ??= loadJson("titles.json", {});
  wingData ??= loadJson("wings.json", {});
  petInfo ??= loadJson("pets.json", {});
  if (!boardNodes) {
    boardNodes = new Map();
    for (const [id, node] of Object.entries(daevanion.nodes)) {
      const list = boardNodes.get(node[0]) ?? [];
      list.push({ id: Number(id), node });
      boardNodes.set(node[0], list);
    }
  }

  itemStats ??= loadJson("item_stats.json", {});
  godstones ??= loadJson("godstones.json", {});
  const statName = (stat: number) => {
    const known = itemStats![String(stat)];
    return { name: known?.names.en ?? `Stat ${stat}`, names: known?.names };
  };
  const gear = (JSON.parse(row.gearJson) as { slot: number; itemId: number; enchant: number; stones?: { stat: number; tier: number }[]; stats?: { stat: number; value: number }[]; godstone?: number; skillBonuses?: { skill: number; level: number }[]; arcana?: { exp: number; god: number; godValue: number; skillBonuses: { skill: number; level: number }[] } }[])
    .map((g) => {
      const info = itemInfo![String(g.itemId)];
      return {
        slot: g.slot,
        slotName: info?.[1] ?? "",
        itemId: g.itemId,
        name: info?.[0] ?? `Item ${g.itemId}`,
        names: info ? itemNamesI18n![info[0]] : undefined,
        icon: itemIcons![String(g.itemId)] ?? null,
        itemLevel: info?.[4] ?? 0,
        grade: info?.[2] ?? 0,
        tier: info?.[3] ?? 0,
        enchant: g.enchant,
        zenit: zenitStage(g.enchant),
        zenitBonus: info?.[1] === "MainHand" && zenitStage(g.enchant) > 0 ? { attack: 10 * zenitStage(g.enchant), damageBoostPercent: zenitStage(g.enchant) } : null,
        stones: g.stones ? g.stones.map((k) => ({ stat: k.stat, ...(k.stat > 0 ? statName(k.stat) : { name: "" }), tier: k.tier, value: (info?.[4] ?? 0) > 0 && info![4] <= LOW_STONE_LEVEL ? itemStats![String(k.stat)]?.stone?.[String(k.tier)] ?? null : null })) : null,
        stats: g.stats ? g.stats.map((k) => ({ stat: k.stat, ...statName(k.stat), value: itemStats![String(k.stat)]?.hundredths === true ? Math.round(k.value / 100) : k.value, percent: itemStats![String(k.stat)]?.percent === true })) : null,
        godstone: g.godstone ? { itemId: g.godstone, name: godstones![String(g.godstone)]?.en ?? `Item ${g.godstone}`, names: godstones![String(g.godstone)] } : null,
        skillBonuses: g.skillBonuses ? g.skillBonuses.map((k) => ({ skill: k.skill, name: skillNames![String(k.skill)] ?? String(k.skill), names: skillNamesI18n![String(k.skill)], level: k.level })) : null,
        arcana: g.arcana
          ? { exp: g.arcana.exp, god: g.arcana.god, godValue: g.arcana.godValue, skillBonuses: g.arcana.skillBonuses.map((k) => ({ skill: k.skill, name: skillNames![String(k.skill)] ?? String(k.skill), names: skillNamesI18n![String(k.skill)], level: k.level })) }
          : null,
      };
    })
    .sort((a, b) => a.slot - b.slot);
  const known = gear.filter((g) => g.itemLevel > 0);

  const skills = skillRows(JSON.parse(row.skillsJson) as StoredSkill[]);
  // Presets only when the character has more than one: a single one is what skills already shows.
  const storedPresets = JSON.parse(row.presetsJson ?? "[]") as { index: number; active?: boolean; seenAt?: string; skills: StoredSkill[] }[];
  const presets =
    storedPresets.length > 1
      ? storedPresets.sort((x, y) => x.index - y.index).map((x) => ({ index: x.index, active: x.active === true, seenAt: x.seenAt ?? null, skills: skillRows(x.skills) }))
      : [];

  const boards = (JSON.parse(row.daevanionJson) as { board: number; nodes: number[] }[]).map((b) => {
    const stats: Record<string, number> = {};
    const bonuses = new Map<number, number>();
    let active = 0;
    let knownNodes = 0;
    let activePoints = 0;
    let totalPoints = 0;
    const cells: [number, number, number, string | number, number, number, number][] = [];
    const activeIds = new Set(b.nodes);
    for (const { id, node } of boardNodes!.get(b.board) ?? []) {
      if (node[4] !== "Start") {
        totalPoints += NODE_GRADE[node[3]] ?? 1;
      }
      const kind = node[4] === "Start" ? 0 : node[4] === "SkillLevel" ? 2 : 1;
      cells.push([node[1], node[2], kind, kind === 2 ? Number(node[5]) : kind === 1 ? node[5] : 0, activeIds.has(id) ? 1 : 0, NODE_GRADE[node[3]] ?? 0, node[6]]);
    }
    for (const id of b.nodes) {
      const node = daevanion!.nodes[String(id)];
      if (node?.[4] === "Start") {
        continue;
      }
      active++;
      if (!node) {
        continue;
      }
      knownNodes++;
      activePoints += NODE_GRADE[node[3]] ?? 1;
      if (node[4] === "SkillLevel") {
        const skillId = Number(node[5]);
        bonuses.set(skillId, (bonuses.get(skillId) ?? 0) + node[6]);
      } else if (node[4] === "Stat") {
        stats[node[5]] = (stats[node[5]] ?? 0) + node[6];
      }
    }
    return {
      board: b.board,
      name: daevanion!.boards[String(b.board)]?.[0] ?? `Board ${b.board}`,
      activeNodes: active,
      knownNodes,
      activePoints,
      totalPoints,
      stats,
      skillBonuses: [...bonuses].map(([id, value]) => ({ id, name: skillNames![String(id)] ?? String(id), names: skillNamesI18n![String(id)], value })),
      cells,
    };
  });

  const species = (JSON.parse(row.speciesJson) as { id: number; level: number; progress: number; effects: { page: number; slot: number; stat: number; value: number; kind?: number }[] }[])
    .map((k) => {
      const info = speciesData!.species[String(k.id)];
      return {
        id: k.id,
        key: info?.key ?? `species${k.id}`,
        names: info?.names ?? {},
        level: k.level,
        progress: k.progress,
        effects: k.effects
          .map((e) => {
            const stat = speciesData!.stats[String(e.stat)];
            return { page: e.page, slot: e.slot, stat: e.stat, name: stat?.names.en ?? `Stat ${e.stat}`, names: stat?.names, value: e.value, percent: stat?.percent === true, kind: e.kind ?? 0 };
          })
          .sort((a, b) => a.page - b.page || a.slot - b.slot),
      };
    })
    .sort((a, b) => a.id - b.id);

  return {
    source: row.source,
    updatedAt: row.updatedAt,
    level: row.level,
    className: row.classId ? (AION2_CLASS_BY_ID[row.classId] ?? null) : null,
    // The faction a fight reported for him; the class bits of the profile are no faction.
    faction: factionFromFights(playerId),
    gear,
    averageItemLevel: known.length > 0 ? Math.round((known.reduce((s, g) => s + g.itemLevel, 0) / known.length) * 10) / 10 : null,
    skills,
    presets,
    daevanion: boards,
    species,
    titles: (JSON.parse(row.titlesJson) as { slot: number; titleId: number }[])
      .map((t) => {
        const info = titleData![String(t.titleId)];
        return { slot: t.slot, titleId: t.titleId, name: info?.n.en ?? `Title ${t.titleId}`, names: info?.n ?? {}, grade: info?.g ?? null };
      })
      .sort((a, b) => a.slot - b.slot),
    pets: (JSON.parse(row.petsJson) as { species: number; level: number; kinds: number[] }[])
      .map((k) => {
        const info = speciesData!.species[String(k.species)];
        return { species: k.species, key: info?.key ?? `species${k.species}`, names: info?.names ?? {}, level: k.level, kinds: k.kinds };
      })
      .sort((a, b) => a.species - b.species),
    ranks: (JSON.parse(row.ranksJson) as { type: number; scope: number; rank: number; points: number; change: number; grade: number; stats?: string; at: string }[]).map((r) => ({
      type: r.type,
      scope: r.scope,
      rank: r.rank,
      points: r.points,
      change: r.change,
      grade: r.grade,
      stats: statsOf(r.stats),
      at: r.at,
    })),
    pantheon: pantheonOf(row.pantheonJson),
    boards: boardSummary(row.boardCountsJson, boards),
    ...buildCharacterView(playerId),
    portraitUrl: null,
  };
}

/** The game's Pantheon tables (Tools/aion2-dat/build_pantheon.py): decoration number -> kind (frame, statue, colossus), race, names and effects, and the names of the stats they raise. */
type PantheonTables = {
  stats: Record<string, Record<string, string>>;
  decorations: Record<string, { k: "frame" | "statue" | "colossus"; r: string; n: Record<string, string>; e: [string, number][] }>;
};
let pantheonTables: PantheonTables | null = null;

/** The order the game's Pantheon window lists the stats in. */
const PANTHEON_STAT_ORDER = ["Illusion", "Life", "Destiny", "Wisdom", "Freedom", "Death", "Space", "Justice", "Time", "Destruction"];

export interface PantheonDecorationView {
  id: number;
  kind: "frame" | "statue" | "colossus";
  names: Record<string, string>;
  effects: { stat: string; value: number }[];
}

export interface PantheonView {
  /** The setups seen, each with its decorations (slot 1-12 frames, 13-16 statues, 17 colossus) and the sum of their effects the way the window shows it. */
  presets: {
    preset: number;
    slots: (PantheonDecorationView & { slot: number })[];
    totals: { stat: string; names: Record<string, string>; value: number }[];
  }[];
  /** Every registered decoration; placed = it stands in a setup seen. */
  owned: (PantheonDecorationView & { placed: boolean })[];
  /** The names of the stats (Illusion, Life ...) in the client's languages. */
  statNames: Record<string, Record<string, string>>;
}

/** The stored Pantheon as the view shows it, with the game's names and effects; null while nothing was read. */
export function pantheonOf(json: string): PantheonView | null {
  const data = mergePantheon(json, {});
  if (data.presets.length === 0 && data.owned.length === 0) {
    return null;
  }

  pantheonTables ??= loadJson("pantheon.json", { stats: {}, decorations: {} });
  const tables = pantheonTables;
  const deco = (id: number): PantheonDecorationView => {
    const row = tables.decorations[String(id)];
    return { id, kind: row?.k ?? (id >= 5000 ? "colossus" : id >= 1000 ? "statue" : "frame"), names: row?.n ?? {}, effects: (row?.e ?? []).map(([stat, value]) => ({ stat, value })) };
  };
  const placed = new Set(data.presets.flatMap((p) => p.slots.map((k) => k.id)));
  return {
    presets: data.presets.map((p) => {
      const slots = p.slots.map((k) => ({ slot: k.slot, ...deco(k.id) }));
      const sums = new Map<string, number>();
      for (const k of slots) for (const e of k.effects) sums.set(e.stat, (sums.get(e.stat) ?? 0) + e.value);
      const order = (stat: string) => (PANTHEON_STAT_ORDER.includes(stat) ? PANTHEON_STAT_ORDER.indexOf(stat) : 99);
      return {
        preset: p.preset,
        slots,
        totals: [...sums.entries()].sort((a, b) => order(a[0]) - order(b[0])).map(([stat, value]) => ({ stat, names: tables.stats[stat] ?? {}, value })),
      };
    }),
    owned: data.owned.map((id) => ({ ...deco(id), placed: placed.has(id) })),
    statNames: tables.stats,
  };
}

/** The game's own stats text of a ranking row ({"kill_count":11, ...}) as numbers; null when it is not that. */
function statsOf(text: string | undefined): Record<string, number> | null {
  try {
    const parsed = text ? (JSON.parse(text) as Record<string, unknown>) : null;
    if (!parsed || typeof parsed !== "object") {
      return null;
    }
    const out: Record<string, number> = {};
    for (const [k, v] of Object.entries(parsed)) {
      if (typeof v === "number" && Number.isFinite(v) && /^[a-z_]{1,32}$/.test(k)) {
        out[k] = v;
      }
    }
    return Object.keys(out).length > 0 ? out : null;
  } catch {
    return null;
  }
}

function attributesOf(json: string): ProfileView["attributes"] {
  const values = JSON.parse(json) as Record<string, number>;
  if (Object.keys(values).length === 0) {
    return null;
  }
  const pick = (list: [number, string][]) => list.map(([id, key]) => ({ id, key, value: values[String(id)] ?? 0 }));
  return { main: pick(MAIN_ATTRIBUTES), lords: pick(LORD_ATTRIBUTES) };
}

function wingOf(id: number | null): ProfileView["wing"] {
  if (!id) {
    return null;
  }
  const info = wingData![String(id)];
  const names = info?.names ?? {};
  return { id, name: names.en ?? `Wings ${id}`, names, icon: info?.icon ?? null, grade: info?.grade ?? null };
}

function activePetOf(id: number | null, level: number | null): ProfileView["activePet"] {
  if (!id) {
    return null;
  }
  const info = petInfo![String(id)];
  const names = info?.n ?? {};
  return { id, name: names.en ?? `Pet ${id}`, names, icon: info ? info.k : null, level };
}

/**
 * Activated nodes per Daevanion board against the board's size (the start node is not counted, as on the board tab).
 * The own character's node lists win; other players only have the count their window showed (start node included).
 */
function boardSummary(boardCountsJson: string, own: { board: number; name: string; activePoints: number; totalPoints: number }[]): ProfileView["boards"] {
  const totalOf = (board: number): number => (boardNodes!.get(board) ?? []).filter((n) => n.node[4] !== "Start").length;
  const nameOf = (board: number): string => daevanion!.boards[String(board)]?.[0] ?? `Board ${board}`;
  if (own.length > 0) {
    return own.map((b) => ({ board: b.board, name: b.name, count: b.activePoints, total: b.totalPoints }));
  }
  return (JSON.parse(boardCountsJson) as { board: number; count: number }[])
    .map((b) => ({ board: b.board, name: nameOf(b.board), count: Math.max(0, b.count - 1), total: totalOf(b.board) }))
    .sort((a, b) => a.board - b.board);
}

/** How long a profile read in the game (somebody inspected the player) stays untouched by NC's page: after this NC's view may replace it. */
export const SEEN_OVERWRITE_AFTER = 2 * 24 * 3_600_000;
/** The same for a profile from the player's own client: it holds on far longer than the 4 days a profile of another player's client does. */
export const SELF_OVERWRITE_AFTER = 7 * 24 * 3_600_000;

/** `updated_at` is stored as "YYYY-MM-DD HH:MM:SS" in UTC. */
const ageOf = (updatedAt: string, now: number): number => now - Date.parse(`${updatedAt.replace(" ", "T")}Z`);

/**
 * Stores what NC's official character page says (gear, skills, Daevanion board counts, level, class, title) as the player's profile, so the
 * profile page shows it like any other. Its rank is the lowest, with three rules:
 * - NC's page always EXTENDS a profile: what the profile lacks (skills, Daevanion nodes, board counts, title) is filled in, nothing present is touched.
 * - A profile from the game ("seen") older than <see SEEN_OVERWRITE_AFTER> is replaced by NC's view (source "nc"); a fresh one stays.
 * - A profile from the player's own client ("self") holds on for SELF_OVERWRITE_AFTER; after that NC's gear, skills, boards and level replace
 *   its own, but what only the client knows (species, ranks, attributes, wings, pets, account key) stays, and the source stays "self".
 * An extension or an overwrite of a self profile never moves `updated_at`: the age is the age of the client's reading, and the next
 * upload from the game replaces NC's view as before (see upsertProfile).
 */
export function upsertNcProfile(
  playerId: number,
  data: Pick<NcCharacterData, "gear" | "skills" | "boardCounts" | "daevanion" | "level" | "classId" | "titleId">,
  now = Date.now(),
): void {
  if (data.gear.length === 0 && data.classId === null) {
    return;
  }
  const row = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get();
  const titles = data.titleId ? [{ slot: 1, titleId: data.titleId }] : [];
  if (row && row.source !== "nc") {
    const age = ageOf(row.updatedAt, now);
    if (row.source === "self" && age > SELF_OVERWRITE_AFTER && data.gear.length > 0) {
      db.update(playerProfiles)
        .set({
          level: data.level ?? row.level,
          classId: data.classId ?? row.classId,
          gearJson: JSON.stringify(data.gear),
          skillsJson: data.skills.length > 0 ? JSON.stringify(data.skills) : row.skillsJson,
          // the presets belong to the skills they were read with: new skills, no presets
          presetsJson: data.skills.length > 0 ? "[]" : row.presetsJson,
          daevanionJson: data.daevanion.length > 0 ? JSON.stringify(data.daevanion) : row.daevanionJson,
          boardCountsJson: data.boardCounts.length > 0 ? JSON.stringify(data.boardCounts) : row.boardCountsJson,
          titlesJson: titles.length > 0 ? JSON.stringify(titles) : row.titlesJson,
        })
        .where(eq(playerProfiles.playerId, playerId))
        .run();
      return;
    }
    if (row.source === "seen" && age > SEEN_OVERWRITE_AFTER && data.gear.length > 0) {
      // falls through to the replacement below
    } else {
      // fresh: only what is missing
      const fill: Partial<typeof playerProfiles.$inferInsert> = {};
      if (row.skillsJson === "[]" && data.skills.length > 0) {
        fill.skillsJson = JSON.stringify(data.skills);
      }
      if (row.daevanionJson === "[]" && data.daevanion.length > 0) {
        fill.daevanionJson = JSON.stringify(data.daevanion);
      }
      if (row.boardCountsJson === "[]" && data.boardCounts.length > 0) {
        fill.boardCountsJson = JSON.stringify(data.boardCounts);
      }
      if (row.titlesJson === "[]" && titles.length > 0) {
        fill.titlesJson = JSON.stringify(titles);
      }
      if (Object.keys(fill).length > 0) {
        db.update(playerProfiles).set(fill).where(eq(playerProfiles.playerId, playerId)).run();
      }
      return;
    }
  }
  const values = {
    source: "nc" as const,
    level: data.level,
    classId: data.classId,
    // The in-game gear score and class bits stay with a replaced "seen" row: they are what the game said, NC has no such number.
    classBits: row?.classBits ?? null,
    gearScore: row?.gearScore ?? null,
    gearJson: JSON.stringify(data.gear),
    skillsJson: JSON.stringify(data.skills),
    daevanionJson: JSON.stringify(data.daevanion),
    speciesJson: "[]",
    titlesJson: JSON.stringify(titles),
    petsJson: "[]",
    boardCountsJson: JSON.stringify(data.boardCounts),
    attributesJson: "{}",
    wingId: null,
    wingSkinId: null,
    activePet: null,
    activePetLevel: null,
  };
  if (row) {
    db.update(playerProfiles).set({ ...values, updatedAt: new Date(now).toISOString().slice(0, 19).replace("T", " ") }).where(eq(playerProfiles.playerId, playerId)).run();
  } else {
    db.insert(playerProfiles).values({ playerId, ...values }).run();
  }
}
