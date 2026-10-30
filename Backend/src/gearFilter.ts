import { sql, type SQL } from "drizzle-orm";
import { encounters } from "./db/schema.js";

/** A range of the gear score (the inspect value of a player's profile) or, with `metric` "power", of the combat power NC's character page shows, or, with "level", of the item level
 * (Gegenstandsstufe) of the same page: the one the instances ask a minimum of, known for far more players than the gear score. Both ends included. */
export interface GearRange {
  min: number;
  max: number;
  /** Absent = the gear score. */
  metric?: "power" | "level";
}

/** "1500-2000", "2000-" (and more) or "-1500" for the gear score; the same with a leading "p" ("p100000-", "p-75000") for the combat power and a leading "i" ("i2750-2850") for the item level. Anything else is no filter. */
export function parseGearRange(raw: string | undefined): GearRange | null {
  const match = /^([pi])?(\d{1,7})?-(\d{1,7})?$/.exec(raw ?? "");
  if (!match || (match[2] === undefined && match[3] === undefined)) {
    return null;
  }
  const power = match[1] === "p";
  // the gear score and the item level have at most five digits; anything longer is neither
  if (!power && ((match[2]?.length ?? 0) > 5 || (match[3]?.length ?? 0) > 5)) {
    return null;
  }
  const min = match[2] === undefined ? 0 : Number(match[2]);
  const max = match[3] === undefined ? Number.MAX_SAFE_INTEGER : Number(match[3]);
  if (min > max) {
    return null;
  }
  return power ? { min, max, metric: "power" } : match[1] === "i" ? { min, max, metric: "level" } : { min, max };
}

export type Metric = "score" | "power" | "level";

/** The metric a range is about. */
export const metricOf = (range: GearRange): Metric => (range.metric === "power" ? "power" : range.metric === "level" ? "level" : "score");

/** The players of the fight with a known value of the metric: the gear score of a seen or own profile, or the combat power NC lists for the character. */
const known = (metric: Metric): SQL =>
  metric === "power"
    ? sql`select 1 from encounter_participants g join player_nc_character gp on gp.player_id = g.player_id where g.encounter_id = ${encounters.id} and gp.combat_power > 0`
    : metric === "level"
      ? sql`select 1 from encounter_participants g join player_nc_character gp on gp.player_id = g.player_id where g.encounter_id = ${encounters.id} and gp.item_level > 0`
      : sql`select 1 from encounter_participants g join player_profiles gp on gp.player_id = g.player_id where g.encounter_id = ${encounters.id} and gp.gear_score > 0`;

const column = (metric: Metric): SQL => (metric === "power" ? sql`gp.combat_power` : metric === "level" ? sql`gp.item_level` : sql`gp.gear_score`);

/**
 * The fights a range keeps: at least one player has a known value and every player whose value is known lies inside the range. Players without a known
 * value neither keep nor drop a fight. No range keeps everything.
 */
export function gearCondition(range: GearRange | null): SQL | undefined {
  if (range === null) {
    return undefined;
  }
  const metric = metricOf(range);
  return sql`(exists (${known(metric)}) and not exists (${known(metric)} and (${column(metric)} < ${range.min} or ${column(metric)} > ${range.max})))`;
}

/** In how many of the fights at least one value is known (for the note next to the filter). */
export function knownGearCondition(metric: Metric = "score"): SQL {
  return sql`exists (${known(metric)})`;
}

/** At least this many known values before the filter offers ranges: with fewer, thirds mean nothing. */
export const MIN_VALUES_FOR_EDGES = 12;

/**
 * The filter's ranges follow what was seen: the two edges that cut the known values into thirds, rounded to a step (50 for the gear score and the item level, 1,000 for the combat power).
 * Fewer than 12 values, or values too close for two edges, give fewer edges (none = no ranges, the row is left out). An edge at either end of the values would make an empty range and is dropped.
 */
export function gearEdges(values: number[], metric: Metric): number[] {
  if (values.length < MIN_VALUES_FOR_EDGES) {
    return [];
  }
  const sorted = [...values].sort((a, b) => a - b);
  const step = metric === "power" ? 1000 : 50;
  const at = (fraction: number) => Math.round(sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * fraction))] / step) * step;
  const low = sorted[0];
  const high = sorted[sorted.length - 1];
  const edges = [...new Set([at(1 / 3), at(2 / 3)])].filter((edge) => edge > low && edge < high);
  return edges.sort((a, b) => a - b);
}
