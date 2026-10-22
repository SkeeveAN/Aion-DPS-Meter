import type { UploadPayload } from "./uploadSchema.js";

/** The pure part of the boss-health rules (no database), shared with scripts/list-partial-candidates.ts. See bossHp.ts for the whole story. */

/**
 * A fight whose highest reading reaches this share of the real maximum is a whole one; below it, a partial fight - unless the group's damage
 * reaches the same share of it (FULL_FIGHT_DAMAGE_SHARE). The first reading of a whole fight comes a moment after the first hits, so a
 * quick fight can read 94 % at the top while the hits already counted add up to the whole boss; someone who really joined late has both low.
 */
export const FULL_FIGHT_HP_SHARE = 0.95;
export const FULL_FIGHT_DAMAGE_SHARE = 0.95;
/** A party-scaled boss has its health multiplied by the party size, at most five. */
export const MAX_PARTY_SCALE = 5;
/** Bosses that grow after the spawn (Talisra, Atiel) may read up to this much above the spawn value. */
export const MAX_HP_GROWTH = 1.2;
/** Two health values this close count as the same one. */
const SAME_HP_TOLERANCE = 0.01;

export interface BossHpRef {
  baseHp: number;
  partyScaled: boolean;
}

export const near = (a: number, b: number) => Math.abs(a - b) <= b * SAME_HP_TOLERANCE;

/** Whether a spawn value fits the reference: the same value, a party multiple of it on a scaled boss, or at most MAX_HP_GROWTH above it. */
export function consistentWithReference(value: number, ref: BossHpRef): boolean {
  if (near(value, ref.baseHp)) {
    return true;
  }
  if (ref.partyScaled) {
    for (let k = 2; k <= MAX_PARTY_SCALE; k++) {
      if (near(value, ref.baseHp * k)) {
        return true;
      }
    }
  }
  return value > ref.baseHp && value <= ref.baseHp * MAX_HP_GROWTH;
}

export interface FightClass {
  isPartial: boolean;
  /** Highest reading / real maximum, 0..1; null when the real maximum is unknown. */
  hpSeenShare: number | null;
  bossRealMaxHp: number | null;
  /** The spawn value as the client sent it. */
  bossSpawnMaxHp: number | null;
}

/**
 * Whole fight or partial fight. `participants` is the number of distinct names in the fight, `worldBoss` an instance of the world boss category
 * (never partial - the old rules hold there), `groupDamage` the damage of all participants (omitted: only the health reading judges).
 * Pure: the reference is passed in (see referenceOf).
 */
export function classifyFight(
  payload: Pick<UploadPayload, "bossMaxHp" | "bossSpawnMaxHp">,
  participants: number,
  ref: BossHpRef | null,
  worldBoss: boolean,
  groupDamage?: number,
): FightClass {
  const spawn = payload.bossSpawnMaxHp ?? null;
  const unknown: FightClass = { isPartial: false, hpSeenShare: null, bossRealMaxHp: null, bossSpawnMaxHp: spawn };
  if (worldBoss || payload.bossMaxHp === undefined) {
    return unknown;
  }
  // A spawn value that does not fit the reference is not trusted; the reference then speaks for the boss.
  const base = spawn !== null && (ref === null || consistentWithReference(spawn, ref)) ? spawn : ref?.baseHp;
  if (base === undefined) {
    return unknown;
  }
  const seen = payload.bossMaxHp;
  let realMax: number;
  if (ref?.partyScaled) {
    const scale = Math.min(MAX_PARTY_SCALE, Math.max(1, Math.max(Math.min(MAX_PARTY_SCALE, participants), Math.round(seen / base))));
    realMax = base * scale;
  } else {
    realMax = Math.max(base, seen);
  }
  const hpSeenShare = Math.min(1, seen / realMax);
  const coveredByDamage = groupDamage !== undefined && groupDamage >= realMax * FULL_FIGHT_DAMAGE_SHARE;
  return { isPartial: hpSeenShare < FULL_FIGHT_HP_SHARE && !coveredByDamage, hpSeenShare, bossRealMaxHp: realMax, bossSpawnMaxHp: spawn };
}
