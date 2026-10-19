import { readFileSync } from "node:fs";
import { inArray } from "drizzle-orm";
import { db } from "./db/client.js";
import { encounterBuffUsage } from "./db/schema.js";

/** A buff column of the encounter page: the player's own, one that came from (or went to) the group, or a consumable (scroll, food). */
export type BuffKind = "own" | "group" | "consumable";

export interface BuffDetail {
  skillId: number;
  /** English name as the client sent it. */
  name: string;
  /** The name in every client language, when the game's tables have it. */
  names?: Record<string, string>;
  /** Icon file (without extension) in /images/aion2/icons/<iconKind>/, or null: the page shows initials. */
  icon: string | null;
  iconKind: "skill" | "item" | null;
  /** Share of the fight the buff was on the player, in thousandths. */
  uptimePermille: number;
  /** How often it was put on. */
  casts: number;
  /** The other players of the fight who gave it; empty = the player's own. */
  givers: string[];
  kind: BuffKind;
  /** Buff food: a player has only one at a time, the page shows all foods in one slot. */
  food: boolean;
}

export interface BuffRowIn {
  skillId: number;
  skill: string;
  casts: number;
  uptimePermille: number;
  givers: string[];
}

/** The potions of the game (item ids 20xxxxx): instant effects, not shown. */
export function isPotion(skillId: number): boolean {
  return skillId >= 2_000_000 && skillId < 2_100_000;
}

/** Scrolls (22xxxxx) and the food effects (21xxxxx, 27xxxxx) the client lists as items' effects. */
export function isConsumable(skillId: number): boolean {
  return (skillId >= 2_100_000 && skillId < 2_300_000) || (skillId >= 2_700_000 && skillId < 2_800_000);
}

export function isFood(skillId: number): boolean {
  return (skillId >= 2_100_000 && skillId < 2_200_000) || (skillId >= 2_700_000 && skillId < 2_800_000);
}

/**
 * Sorts the buffs of an encounter's players into the page's three kinds. A buff counts as a group buff for EVERYONE once any player of the fight got it from
 * another player of the fight (a Chanter's mantra, a Cleric's light, a Gladiator's counterstrike - also for the one who cast it). A buff whose givers are all nobody
 * of the fight came from a monster and is dropped, and so are potions and rows without a usable id.
 */
export function classifyEncounterBuffs(players: { id: number; name: string; buffs: BuffRowIn[] }[]): Map<number, (BuffRowIn & { kind: BuffKind })[]> {
  const names = new Set(players.map((p) => p.name));
  const groupBuffs = new Set<string>();
  for (const p of players) {
    for (const b of p.buffs) {
      if (b.skillId >= 1000 && !isPotion(b.skillId) && b.givers.some((g) => g !== p.name && names.has(g))) {
        groupBuffs.add(b.skill);
      }
    }
  }

  const result = new Map<number, (BuffRowIn & { kind: BuffKind })[]>();
  for (const p of players) {
    const list: (BuffRowIn & { kind: BuffKind })[] = [];
    for (const b of p.buffs) {
      if (b.skillId < 1000 || isPotion(b.skillId)) {
        continue;
      }
      const givers = b.givers.filter((g) => names.has(g));
      if (b.givers.length > 0 && givers.length === 0) {
        continue; // given by a monster
      }
      const kind: BuffKind = givers.length > 0 || groupBuffs.has(b.skill) ? "group" : isConsumable(b.skillId) ? "consumable" : "own";
      list.push({ ...b, givers, kind });
    }
    result.set(p.id, list);
  }
  return result;
}

// ---- icons and names ---------------------------------------------------------------------------------------------------------------------------------------

function loadJson<T>(file: string, fallback: T): T {
  try {
    return JSON.parse(readFileSync(new URL(`./data/aion2/${file}`, import.meta.url), "utf8")) as T;
  } catch {
    return fallback;
  }
}

let skillIconTokens: Record<string, string> | null = null; // skill id / 10000 -> ICON_xx_SKILL_nnn
let effectIcons: Record<string, string> | null = null; // effect id (a consumable) -> the item's icon name
let skillNamesI18n: Record<string, Record<string, string>> | null = null;
let itemNamesI18n: Record<string, Record<string, string>> | null = null;

export function buffIcon(skillId: number): { icon: string; iconKind: "skill" | "item" } | null {
  skillIconTokens ??= loadJson<Record<string, string>>("skill_icon_tokens.json", {});
  effectIcons ??= loadJson<Record<string, string>>("effect_icons.json", {});
  if (skillId >= 10_000_000) {
    const token = skillIconTokens[String(Math.floor(skillId / 10000))];
    if (token) {
      return { icon: token, iconKind: "skill" };
    }
  }
  for (const id of [skillId, Math.floor(skillId / 10), Math.floor(skillId / 100)]) {
    const item = effectIcons[String(id)];
    if (id > 0 && item) {
      return { icon: item, iconKind: "item" };
    }
  }
  // an effect of a skill the token table has no row for (small ids): the skill's own icon, if it has one
  const token = skillIconTokens[String(Math.floor(skillId / 10000))];
  return token ? { icon: token, iconKind: "skill" } : null;
}

export function buffNames(skillId: number, englishName: string): Record<string, string> | undefined {
  skillNamesI18n ??= loadJson<Record<string, Record<string, string>>>("skill_names_i18n.json", {});
  itemNamesI18n ??= loadJson<Record<string, Record<string, string>>>("item_names_i18n.json", {});
  return skillNamesI18n[String(skillId)] ?? skillNamesI18n[String(Math.floor(skillId / 10000) * 10000)] ?? itemNamesI18n[englishName];
}

// ---- the database -------------------------------------------------------------------------------------------------------------------------------------------

/** The stored buff rows of these participants (by participant id). */
export function buffRowsByParticipant(participantIds: number[]): Map<number, BuffRowIn[]> {
  const result = new Map<number, BuffRowIn[]>();
  if (participantIds.length === 0) {
    return result;
  }

  const rows = db.select().from(encounterBuffUsage).where(inArray(encounterBuffUsage.participantId, participantIds)).all();
  for (const r of rows) {
    if (r.skillId === 0) {
      continue; // sent by a client that does not send the detail: only the old top list has it
    }
    let givers: string[] = [];
    try {
      const parsed: unknown = JSON.parse(r.giversJson);
      givers = Array.isArray(parsed) ? parsed.filter((g): g is string => typeof g === "string") : [];
    } catch {
      givers = [];
    }
    const list = result.get(r.participantId) ?? [];
    list.push({ skillId: r.skillId, skill: r.skillName, casts: r.casts, uptimePermille: r.uptimePermille, givers });
    result.set(r.participantId, list);
  }
  return result;
}

/** The buffs of every participant of an encounter, sorted into the page's kinds, with icons and names. Keyed by participant id. */
export function encounterBuffDetails(roster: { participantId: number; playerName: string }[]): Map<number, BuffDetail[]> {
  const rows = buffRowsByParticipant(roster.map((r) => r.participantId));
  const classified = classifyEncounterBuffs(roster.map((r) => ({ id: r.participantId, name: r.playerName, buffs: rows.get(r.participantId) ?? [] })));
  const result = new Map<number, BuffDetail[]>();
  for (const r of roster) {
    const list = (classified.get(r.participantId) ?? []).map((b) => {
      const art = buffIcon(b.skillId);
      return {
        skillId: b.skillId,
        name: b.skill,
        names: buffNames(b.skillId, b.skill),
        icon: art?.icon ?? null,
        iconKind: art?.iconKind ?? null,
        uptimePermille: b.uptimePermille,
        casts: b.casts,
        givers: b.givers,
        kind: b.kind,
        food: isFood(b.skillId),
      } satisfies BuffDetail;
    });
    result.set(r.participantId, list.sort((a, b) => b.uptimePermille - a.uptimePermille));
  }
  return result;
}
