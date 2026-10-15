import { and, asc, eq } from "drizzle-orm";
import { db } from "./db/client.js";
import { bosses } from "./db/schema.js";
import { classBoxes, statsForBossIds } from "./routes/bosses.js";

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
}

/** The statistics page of a dungeon: per boss the runs, best group iDPS, average kill time and the class medians, and the class share of the whole dungeon. */
export function instanceStatistics(instanceId: number, serverId: number | null, mode: string | null): InstanceStatistics {
  const rows = db
    .select({ id: bosses.id, slug: bosses.slug, name: bosses.name, nameEn: bosses.nameEn })
    .from(bosses)
    .where(and(eq(bosses.instanceId, instanceId), eq(bosses.isTrashMob, false)))
    .orderBy(asc(bosses.name))
    .all();
  const share = new Map<string, number>();
  const result: InstanceBossStats[] = [];
  for (const boss of rows) {
    const stats = statsForBossIds([boss.id], serverId, mode);
    const boxes = classBoxes(boss.id, serverId, mode);
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
  return { bosses: result, classShare: [...share.entries()].map(([className, samples]) => ({ className, samples })).sort((a, b) => b.samples - a.samples) };
}
