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

export type ProfileView = {
  source: "self" | "seen";
  updatedAt: string;
  level: number | null;
  className: string | null;
  faction: "Elyos" | "Asmodian" | null;
  gear: { slot: number; slotName: string; itemId: number; name: string; icon: string | null; itemLevel: number; grade: number; tier: number; enchant: number }[];
  averageItemLevel: number | null;
  skills: { id: number; name: string; names?: Record<string, string>; icon: string | null; passive: boolean; level: number; baseLevel: number }[];
  daevanion: {
    board: number;
    name: string;
    activeNodes: number;
    knownNodes: number;
    stats: Record<string, number>;
    skillBonuses: { id: number; name: string; names?: Record<string, string>; value: number }[];
    /** Every node of the board map: [row, col, kind, skillId, active] (1-based; kind 0 start, 1 stat, 2 skill level; skillId only for kind 2, else 0; active 1/0). */
    cells: [number, number, number, number, number][];
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
  const skills = (JSON.parse(row.skillsJson) as { id: number; level: number; baseLevel: number }[])
    .filter((s) => s.id % 10000 === 0)
    .map((s) => ({ id: s.id, name: skillNames![String(s.id)] ?? String(s.id), names: skillNamesI18n![String(s.id)], icon: skillIcons![String(s.id)] ?? null, passive: skillTypes![String(s.id)] === "p", level: s.level, baseLevel: s.baseLevel }))
    .sort((a, b) => b.level - a.level || a.name.localeCompare(b.name));

  const boards = (JSON.parse(row.daevanionJson) as { board: number; nodes: number[] }[]).map((b) => {
    const stats: Record<string, number> = {};
    const bonuses = new Map<number, number>();
    let active = 0;
    let knownNodes = 0;
    const cells: [number, number, number, number, number][] = [];
    const activeIds = new Set(b.nodes);
    for (const { id, node } of boardNodes!.get(b.board) ?? []) {
      cells.push([node[1], node[2], node[4] === "Start" ? 0 : node[4] === "SkillLevel" ? 2 : 1, node[4] === "SkillLevel" ? Number(node[5]) : 0, activeIds.has(id) ? 1 : 0]);
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
