import { and, asc, eq } from "drizzle-orm";
import { db } from "./db/client.js";
import { bosses } from "./db/schema.js";
import { classBoxes, gearCoverage, gearValues, statsForBossIds } from "./routes/bosses.js";
import { gearEdges, type Metric } from "./gearFilter.js";
import type { GearRange } from "./gearFilter.js";

/** One boss of a dungeon in the dungeon's statistics: its fight numbers and the median iDPS of each class on it. */
export interface InstanceBossStats {
  slug: string | null;
  name: string;
  nameEn: string | null;
  runCount: number;
  bestIdps: number | null;
  avgDurationSeconds: number | null;
  classes: { className: string; samples: number; median: number }[];
}

export interface InstanceStatistics {
  bosses: InstanceBossStats[];
  /** How many counted rows each class has across all the dungeon's bosses (what is played there), most first. */
  classShare: { className: string; samples: number }[];
  /** How many fights have a known gear score (what a gear score filter can work with) and the range in use. */
  gear: { runs: number; known: number; edges: number[]; selected: GearRange | null };
  /** The same for the combat power NC lists (the second filter). */
  power: { runs: number; known: number; edges: number[]; selected: GearRange | null };
  /** The same for the item level NC lists (the third filter). `edges` cut the known values into thirds: the ranges the filter offers. */
  level: { runs: number; known: number; edges: number[]; selected: GearRange | null };
}

/** The statistics page of a dungeon: per boss the runs, best group iDPS, average kill time and the class medians, and the class share of the whole dungeon. */
export function instanceStatistics(instanceId: number, serverId: number | null, mode: string | null, gear: GearRange | null = null): InstanceStatistics {
  const rows = db
    .select({ id: bosses.id, slug: bosses.slug, name: bosses.name, nameEn: bosses.nameEn })
    .from(bosses)
    .where(and(eq(bosses.instanceId, instanceId), eq(bosses.isTrashMob, false)))
    .orderBy(asc(bosses.name))
    .all();
  const share = new Map<string, number>();
  let coverage = { runs: 0, known: 0 };
  let powerCoverage = { runs: 0, known: 0 };
  let levelCoverage = { runs: 0, known: 0 };
  const values: Record<Metric, number[]> = { score: [], power: [], level: [] };
  const result: InstanceBossStats[] = [];
  for (const boss of rows) {
    const bossCoverage = gearCoverage(boss.id, serverId, mode);
    coverage = { runs: coverage.runs + bossCoverage.runs, known: coverage.known + bossCoverage.known };
    const bossPower = gearCoverage(boss.id, serverId, mode, "power");
    powerCoverage = { runs: powerCoverage.runs + bossPower.runs, known: powerCoverage.known + bossPower.known };
    const bossLevel = gearCoverage(boss.id, serverId, mode, "level");
    levelCoverage = { runs: levelCoverage.runs + bossLevel.runs, known: levelCoverage.known + bossLevel.known };
    for (const metric of ["score", "power", "level"] as const) {
      values[metric].push(...gearValues(boss.id, serverId, mode, metric));
    }
    const stats = statsForBossIds([boss.id], serverId, mode, gear);
    const boxes = classBoxes(boss.id, serverId, mode, gear);
    for (const box of boxes) {
      share.set(box.className, (share.get(box.className) ?? 0) + box.samples);
    }
    result.push({
      slug: boss.slug,
      name: boss.name,
      nameEn: boss.nameEn,
      runCount: stats.runCount,
      bestIdps: stats.bestIdps,
      avgDurationSeconds: stats.avgDurationSeconds,
      classes: boxes.map((b) => ({ className: b.className, samples: b.samples, median: b.median })),
    });
  }
  return { bosses: result, classShare: [...share.entries()].map(([className, samples]) => ({ className, samples })).sort((a, b) => b.samples - a.samples), gear: { ...coverage, edges: gearEdges(values.score, "score"), selected: gear?.metric === undefined ? gear : null }, power: { ...powerCoverage, edges: gearEdges(values.power, "power"), selected: gear?.metric === "power" ? gear : null }, level: { ...levelCoverage, edges: gearEdges(values.level, "level"), selected: gear?.metric === "level" ? gear : null } };
}
