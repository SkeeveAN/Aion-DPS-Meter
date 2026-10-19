import { sql, type SQL } from "drizzle-orm";
import { encounters } from "./db/schema.js";

/** A range of the gear score (the inspect value of a player's profile), both ends included. */
export interface GearRange {
  min: number;
  max: number;
}

/** "1500-2000", "2000-" (and more) or "-1500"; anything else is no filter. */
export function parseGearRange(raw: string | undefined): GearRange | null {
  const match = /^(\d{1,5})?-(\d{1,5})?$/.exec(raw ?? "");
  if (!match || (match[1] === undefined && match[2] === undefined)) {
    return null;
  }
  const min = match[1] === undefined ? 0 : Number(match[1]);
  const max = match[2] === undefined ? Number.MAX_SAFE_INTEGER : Number(match[2]);
  return min <= max ? { min, max } : null;
}

const KNOWN = sql`select 1 from encounter_participants g join player_profiles gp on gp.player_id = g.player_id where g.encounter_id = ${encounters.id} and gp.gear_score > 0`;

/**
 * The fights a gear score range keeps: at least one player has a known gear score and every player whose gear score is known
 * lies inside the range. Players without a known gear score neither keep nor drop a fight. No range keeps everything.
 */
export function gearCondition(range: GearRange | null): SQL | undefined {
  if (range === null) {
    return undefined;
  }
  return sql`(exists (${KNOWN}) and not exists (${KNOWN} and (gp.gear_score < ${range.min} or gp.gear_score > ${range.max})))`;
}

/** In how many of the fights at least one gear score is known (for the note next to the filter). */
export function knownGearCondition(): SQL {
  return sql`exists (${KNOWN})`;
}
