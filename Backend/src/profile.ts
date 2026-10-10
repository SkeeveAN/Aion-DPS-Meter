import { readFileSync } from "node:fs";
import { desc, and, eq, ne } from "drizzle-orm";
import { z } from "zod";
import { AION2_CLASS_BY_ID } from "./constants.js";
import { db } from "./db/client.js";
import { encounterParticipants, playerNcCharacter, playerProfiles } from "./db/schema.js";

/**
 * Character profile of an Aion 2 player, as an uploading client read it from the game's traffic.
 * Ids only: item names, item levels, skill names and Daevanion effects are resolved from the game
 * data when the profile is read (see buildProfileView), so correcting that data needs no migration.
 *
 * "self" is the uploader's own character (level, full equipment with enchants, skills, Daevanion);
 * "seen" is what a client could read off another player (class, faction and the visible equipment).
 */
export const profileSchema = z.object({
  source: z.enum(["self", "seen"]),
  level: z.number().int().min(1).max(200).optional(),
  classId: z.number().int().min(1).max(9).optional(),
  // The low two bits of the class code (4 * class id + bits). Not a faction. Clients up to 0.27 sent them as "faction".
  classBits: z.number().int().min(1).max(2).optional(),
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
      }),
    )
    // 25 pieces seen on a real character (slots up to 29); the cap only fends off nonsense.
    .max(64)
    .default([]),
  skills: z
    .array(
      z.object({
        id: z.number().int().min(1).max(2_000_000_000),
        level: z.number().int().min(0).max(60),
        baseLevel: z.number().int().min(0).max(60).default(0),
        // Stigmas of the uploader's own character (the skills the game lists with five variants).
        stigma: z.boolean().default(false),
        // On the first macro page of the uploader's skill bar (what the game's skill window lists as equipped).
        equipped: z.boolean().default(false),
      }),
    )
    .max(400)
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
});

export type ProfileUpload = z.infer<typeof profileSchema>;

/**
 * Stores a profile for a player. The newest data wins - except that a "seen" profile never replaces
 * a "self" one (the uploader's own record is the richer, authoritative one; a stranger's clients
 * only see part of it), and a "seen" profile that carries nothing (no gear, no class) is dropped.
 */
export function upsertProfile(playerId: number, input: Partial<ProfileUpload> & Pick<ProfileUpload, "source">): void {
  // The upload route has already run profileSchema (which fills these defaults); callers that go
  // straight to the merge code may omit the lists.
  const profile = { ...input, gear: input.gear ?? [], skills: input.skills ?? [], daevanion: input.daevanion ?? [], species: input.species ?? [], titles: input.titles ?? [], pets: input.pets ?? [], boardCounts: input.boardCounts ?? [] };
  if (profile.source === "seen" && profile.gear.length === 0 && profile.classId === undefined && profile.level === undefined) {
    return;
  }

  const existing = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get();
  if (existing?.source === "self" && profile.source === "seen") {
    return;
  }

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
    daevanionJson: profile.daevanion.length > 0 ? JSON.stringify(profile.daevanion) : (existing?.daevanionJson ?? "[]"),
    speciesJson: profile.species.length > 0 ? JSON.stringify(profile.species) : (existing?.speciesJson ?? "[]"),
    // Titles, pets and board counts: an upload that lacks them (older client, a window without that part) keeps what is stored.
    titlesJson: profile.titles.length > 0 ? JSON.stringify(profile.titles) : (existing?.titlesJson ?? "[]"),
    petsJson: profile.pets.length > 0 ? JSON.stringify(profile.pets) : (existing?.petsJson ?? "[]"),
    boardCountsJson: profile.boardCounts.length > 0 ? JSON.stringify(profile.boardCounts) : (existing?.boardCountsJson ?? "[]"),
    // Character window (own character): an upload without attributes, wing or pet (older client, window not read) keeps what is stored.
    attributesJson: profile.attributes && Object.keys(profile.attributes).length > 0 ? JSON.stringify(profile.attributes) : (existing?.attributesJson ?? "{}"),
    wingId: profile.wingId ? profile.wingId : (existing?.wingId ?? null),
    wingSkinId: profile.wingSkinId ? profile.wingSkinId : (existing?.wingSkinId ?? null),
    activePet: profile.activePet ? profile.activePet : (existing?.activePet ?? null),
    // The level belongs to the pet it was read for: a new pet without a level does not inherit the old pet's.
    activePetLevel: profile.activePetLevel ? profile.activePetLevel : (!profile.activePet || profile.activePet === existing?.activePet ? (existing?.activePetLevel ?? null) : null),
  };
  if (existing) {
    db.update(playerProfiles)
      .set({ ...values, updatedAt: new Date().toISOString().slice(0, 19).replace("T", " ") })
      .where(eq(playerProfiles.playerId, playerId))
      .run();
  } else {
    db.insert(playerProfiles).values({ playerId, ...values }).run();
  }
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
let itemStats: Record<string, { names: Record<string, string>; percent?: boolean; stone?: Record<string, number> }> | null = null;
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
  const names = loadJson<Record<string, string>>("skill_names.json", {});
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
  source: "self" | "seen";
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
  }[];
  averageItemLevel: number | null;
  skills: { id: number; name: string; names?: Record<string, string>; icon: string | null; passive: boolean; stigma: boolean; equipped: boolean; level: number; baseLevel: number }[];
  daevanion: {
    board: number;
    name: string;
    activeNodes: number;
    knownNodes: number;
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
  /** One circle per species: the quality of each effect slot (0 empty, 1 white, 2 green, 3 blue, 4 gold, 5 orange). */
  pets: { species: number; key: string; names: Record<string, string>; level: number; kinds: number[] }[];
  /** Activated nodes per Daevanion board next to the board's size; for the own character from the node lists. */
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

export type CharacterView = Pick<ProfileView, "attributes" | "wing" | "wingSkin" | "activePet" | "characterSource" | "characterFetchedAt">;

type ClientCharacter = { attributesJson: string; wingId: number | null; wingSkinId: number | null; activePet: number | null; activePetLevel: number | null };

/**
 * Character window data of a player from both sources. What the player's own client uploaded (profile source "self") wins
 * per field; whatever it lacks is filled from NC's official character page. Pure, for tests.
 */
export function mergeCharacter(
  client: (ClientCharacter & { source: "self" | "seen"; updatedAt: string }) | null | undefined,
  nc: (ClientCharacter & { fetchedAt: number }) | null | undefined,
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
  const gear = (JSON.parse(row.gearJson) as { slot: number; itemId: number; enchant: number; stones?: { stat: number; tier: number }[]; stats?: { stat: number; value: number }[]; godstone?: number; skillBonuses?: { skill: number; level: number }[] }[])
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
        stones: g.stones ? g.stones.map((k) => ({ stat: k.stat, ...(k.stat > 0 ? statName(k.stat) : { name: "" }), tier: k.tier, value: itemStats![String(k.stat)]?.stone?.[String(k.tier)] ?? null })) : null,
        stats: g.stats ? g.stats.map((k) => ({ stat: k.stat, ...statName(k.stat), value: k.value, percent: itemStats![String(k.stat)]?.percent === true })) : null,
        godstone: g.godstone ? { itemId: g.godstone, name: godstones![String(g.godstone)]?.en ?? `Item ${g.godstone}`, names: godstones![String(g.godstone)] } : null,
        skillBonuses: g.skillBonuses ? g.skillBonuses.map((k) => ({ skill: k.skill, name: skillNames![String(k.skill)] ?? String(k.skill), names: skillNamesI18n![String(k.skill)], level: k.level })) : null,
      };
    })
    .sort((a, b) => a.slot - b.slot);
  const known = gear.filter((g) => g.itemLevel > 0);

  // Only the base entries (id ends in 0000): the list also carries each skill's specialisation
  // variants, which would repeat the same name.
  const skills = (JSON.parse(row.skillsJson) as { id: number; level: number; baseLevel: number; stigma?: boolean; equipped?: boolean }[])
    .filter((s) => s.id % 10000 === 0)
    .map((s) => ({ id: s.id, name: skillNames![String(s.id)] ?? String(s.id), names: skillNamesI18n![String(s.id)], icon: skillIcons![String(s.id)] ?? null, passive: skillTypes![String(s.id)] === "p", stigma: s.stigma === true, equipped: s.equipped === true, level: s.level, baseLevel: s.baseLevel }))
    .sort((a, b) => b.level - a.level || a.name.localeCompare(b.name));

  const boards = (JSON.parse(row.daevanionJson) as { board: number; nodes: number[] }[]).map((b) => {
    const stats: Record<string, number> = {};
    const bonuses = new Map<number, number>();
    let active = 0;
    let knownNodes = 0;
    const cells: [number, number, number, string | number, number, number, number][] = [];
    const activeIds = new Set(b.nodes);
    for (const { id, node } of boardNodes!.get(b.board) ?? []) {
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
    boards: boardSummary(row.boardCountsJson, boards),
    ...buildCharacterView(playerId),
    portraitUrl: null,
  };
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
function boardSummary(boardCountsJson: string, own: { board: number; name: string; activeNodes: number }[]): ProfileView["boards"] {
  const totalOf = (board: number): number => (boardNodes!.get(board) ?? []).filter((n) => n.node[4] !== "Start").length;
  const nameOf = (board: number): string => daevanion!.boards[String(board)]?.[0] ?? `Board ${board}`;
  if (own.length > 0) {
    return own.map((b) => ({ board: b.board, name: b.name, count: b.activeNodes, total: totalOf(b.board) }));
  }
  return (JSON.parse(boardCountsJson) as { board: number; count: number }[])
    .map((b) => ({ board: b.board, name: nameOf(b.board), count: Math.max(0, b.count - 1), total: totalOf(b.board) }))
    .sort((a, b) => a.board - b.board);
}
