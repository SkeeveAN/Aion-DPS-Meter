import { sql, type SQL } from "drizzle-orm";
import { encounters } from "./db/schema.js";

/** A range of the gear score (the inspect value of a player's profile) or, with `metric` "power", of the combat power NC's character page shows; both ends included. */
export interface GearRange {
  min: number;
  max: number;
  /** Absent = the gear score. */
  metric?: "power";
}

/** "1500-2000", "2000-" (and more) or "-1500" for the gear score; the same with a leading "p" ("p100000-", "p-75000") for the combat power. Anything else is no filter. */
export function parseGearRange(raw: string | undefined): GearRange | null {
  const match = /^(p)?(\d{1,7})?-(\d{1,7})?$/.exec(raw ?? "");
  if (!match || (match[2] === undefined && match[3] === undefined)) {
    return null;
  }
  const power = match[1] === "p";
  // the gear score has at most five digits; anything longer is no gear score
  if (!power && ((match[2]?.length ?? 0) > 5 || (match[3]?.length ?? 0) > 5)) {
    return null;
  }
  const min = match[2] === undefined ? 0 : Number(match[2]);
  const max = match[3] === undefined ? Number.MAX_SAFE_INTEGER : Number(match[3]);
  if (min > max) {
    return null;
  }
  return power ? { min, max, metric: "power" } : { min, max };
}

type Metric = "score" | "power";

/** The players of the fight with a known value of the metric: the gear score of a seen or own profile, or the combat power NC lists for the character. */
const known = (metric: Metric): SQL =>
  metric === "power"
    ? sql`select 1 from encounter_participants g join player_nc_character gp on gp.player_id = g.player_id where g.encounter_id = ${encounters.id} and gp.combat_power > 0`
    : sql`select 1 from encounter_participants g join player_profiles gp on gp.player_id = g.player_id where g.encounter_id = ${encounters.id} and gp.gear_score > 0`;

const column = (metric: Metric): SQL => (metric === "power" ? sql`gp.combat_power` : sql`gp.gear_score`);

/**
 * The fights a range keeps: at least one player has a known value and every player whose value is known lies inside the range. Players without a known
 * value neither keep nor drop a fight. No range keeps everything.
 */
export function gearCondition(range: GearRange | null): SQL | undefined {
  if (range === null) {
    return undefined;
  }
  const metric: Metric = range.metric === "power" ? "power" : "score";
  return sql`(exists (${known(metric)}) and not exists (${known(metric)} and (${column(metric)} < ${range.min} or ${column(metric)} > ${range.max})))`;
}

/** In how many of the fights at least one value is known (for the note next to the filter). */
export function knownGearCondition(metric: Metric = "score"): SQL {
  return sql`exists (${known(metric)})`;
}
