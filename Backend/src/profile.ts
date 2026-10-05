import { readFileSync } from "node:fs";
import { eq } from "drizzle-orm";
import { z } from "zod";
import { AION2_CLASS_BY_ID } from "./constants.js";
import { db } from "./db/client.js";
import { playerProfiles } from "./db/schema.js";

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
  faction: z.number().int().min(1).max(2).optional(),
  gear: z
    .array(
      z.object({
        slot: z.number().int().min(0).max(40),
        itemId: z.number().int().min(1).max(2_000_000_000),
        enchant: z.number().int().min(0).max(30).default(0),
      }),
    )
    .max(24)
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
    .max(150)
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
  const profile = { ...input, gear: input.gear ?? [], skills: input.skills ?? [], daevanion: input.daevanion ?? [] };
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
    faction: profile.faction ?? existing?.faction ?? null,
    gearJson: JSON.stringify(profile.gear),
    // The skill list and the Daevanion boards only exist for "self"; a later upload from the same
    // person that lacks them (their client started mid-session) must not wipe what was stored.
    skillsJson: profile.skills.length > 0 ? JSON.stringify(profile.skills) : (existing?.skillsJson ?? "[]"),
    daevanionJson: profile.daevanion.length > 0 ? JSON.stringify(profile.daevanion) : (existing?.daevanionJson ?? "[]"),
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
let skillIcons: Record<string, string> | null = null;
let skillTypes: Record<string, string> | null = null; // "a" active, "p" passive
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

export type ProfileView = {
  source: "self" | "seen";
  updatedAt: string;
  level: number | null;
  className: string | null;
  faction: "Elyos" | "Asmodian" | null;
  gear: { slot: number; slotName: string; itemId: number; name: string; icon: string | null; itemLevel: number; grade: number; tier: number; enchant: number }[];
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
};

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
  skillIcons ??= loadJson<Record<string, string>>("skill_icons.json", {});
  skillTypes ??= loadJson<Record<string, string>>("skill_types.json", {});
  if (!boardNodes) {
    boardNodes = new Map();
    for (const [id, node] of Object.entries(daevanion.nodes)) {
      const list = boardNodes.get(node[0]) ?? [];
      list.push({ id: Number(id), node });
      boardNodes.set(node[0], list);
    }
  }

  const gear = (JSON.parse(row.gearJson) as { slot: number; itemId: number; enchant: number }[])
    .map((g) => {
      const info = itemInfo![String(g.itemId)];
      return {
        slot: g.slot,
        slotName: info?.[1] ?? "",
        itemId: g.itemId,
        name: info?.[0] ?? `Item ${g.itemId}`,
        icon: itemIcons![String(g.itemId)] ?? null,
        itemLevel: info?.[4] ?? 0,
        grade: info?.[2] ?? 0,
        tier: info?.[3] ?? 0,
        enchant: g.enchant,
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

  return {
    source: row.source,
    updatedAt: row.updatedAt,
    level: row.level,
    className: row.classId ? (AION2_CLASS_BY_ID[row.classId] ?? null) : null,
    // Only 2 (Elyos) is verified; 1 also occurs inside an Elyos legion, so its meaning is unknown.
    faction: row.faction === 2 ? "Elyos" : null,
    gear,
    averageItemLevel: known.length > 0 ? Math.round((known.reduce((s, g) => s + g.itemLevel, 0) / known.length) * 10) / 10 : null,
    skills,
    daevanion: boards,
  };
}
