/** One class on one boss: how the iDPS of every counted row spreads (the box plot of the boss page's statistics view). */
export interface ClassBox {
  className: string;
  samples: number;
  min: number;
  q1: number;
  median: number;
  q3: number;
  max: number;
  /** The values themselves (ascending, at most {@link MAX_VALUES}, evenly thinned out), for the dots behind the box. */
  values: number[];
}

const MAX_VALUES = 120;

/** At most `limit` values of an ascending list, evenly spread, the first and the last always among them. */
export function thinOut(sorted: number[], limit = MAX_VALUES): number[] {
  if (sorted.length <= limit) {
    return [...sorted];
  }
  return Array.from({ length: limit }, (_, i) => sorted[Math.round((i * (sorted.length - 1)) / (limit - 1))]!);
}

/** The p-quantile (0..1) of an ascending list, interpolated between neighbours. */
export function quantile(sorted: number[], p: number): number {
  if (sorted.length === 0) {
    return 0;
  }
  const at = (sorted.length - 1) * p;
  const lo = Math.floor(at);
  const hi = Math.ceil(at);
  return sorted[lo]! + (sorted[hi]! - sorted[lo]!) * (at - lo);
}

/** Boxes per class from the rows (class, iDPS); rows without damage do not count; strongest median first. */
export function boxesByClass(rows: { className: string; idps: number }[]): ClassBox[] {
  const byClass = new Map<string, number[]>();
  for (const row of rows) {
    if (row.idps > 0) {
      (byClass.get(row.className) ?? byClass.set(row.className, []).get(row.className)!).push(row.idps);
    }
  }
  return [...byClass.entries()]
    .map(([className, values]) => {
      const sorted = values.sort((a, b) => a - b);
      return {
        className,
        samples: sorted.length,
        min: sorted[0]!,
        q1: quantile(sorted, 0.25),
        median: quantile(sorted, 0.5),
        q3: quantile(sorted, 0.75),
        max: sorted[sorted.length - 1]!,
        values: thinOut(sorted),
      };
    })
    .sort((a, b) => b.median - a.median);
}

/** What one class did at one boss, skill by skill (the skill table of the boss page's statistics view). */
export interface SkillStat {
  skill: string;
  /** Share of the class's damage on this boss, 0..1. */
  damageShare: number;
  /** In how many of the class's rows the skill was used, 0..1. */
  runShare: number;
  /** Hits per row that used the skill. */
  hitsPerRun: number;
  /** Crit hits of all hits, 0..1. */
  critRate: number;
  averageHit: number;
  /** Of the uses of this skill that had a chain follow-up, the share that were followed in time, 0..1; null when there are too few. */
  chainRate: number | null;
  /** The average level of the skill across the class's rows whose character is known (the game's own list), one decimal; null when too few. */
  avgLevel: number | null;
}

export interface ClassSkills {
  className: string;
  samples: number;
  skills: SkillStat[];
}

const SKILLS_PER_CLASS = 12;
const MIN_ROWS_FOR_SKILLS = 3;
/** A skill used by fewer of the rows than this is left out (a rare or misread skill says nothing about the class). */
const MIN_RUN_SHARE = 0.1;

/**
 * The skills of every class from per-skill totals (rows that used the skill, hits, crit hits, damage) and the number of rows
 * (participants with damage) per class. Strongest skill first; a class with too few rows has no table.
 */
export function skillStatsByClass(
  skillRows: { className: string; skill: string; runs: number; hits: number; critHits: number; damage: number }[],
  samples: Map<string, number>,
  /** Per class and skill name: chain uses offered and taken, and the sum and count of the characters' skill levels. */
  extras: Map<string, { chainOffered: number; chainTaken: number; levelSum: number; levelCount: number }> = new Map(),
): ClassSkills[] {
  const byClass = new Map<string, typeof skillRows>();
  for (const row of skillRows) {
    if (row.damage > 0 && row.hits > 0 && row.skill !== "") {
      (byClass.get(row.className) ?? byClass.set(row.className, []).get(row.className)!).push(row);
    }
  }
  const result: ClassSkills[] = [];
  for (const [className, rows] of byClass) {
    const rowCount = samples.get(className) ?? 0;
    const total = rows.reduce((sum, r) => sum + r.damage, 0);
    if (rowCount < MIN_ROWS_FOR_SKILLS || total <= 0) {
      continue;
    }
    const skills = rows
      .filter((r) => r.runs / rowCount >= MIN_RUN_SHARE)
      .sort((a, b) => b.damage - a.damage)
      .slice(0, SKILLS_PER_CLASS)
      .map((r) => ({
        skill: r.skill,
        damageShare: r.damage / total,
        runShare: Math.min(1, r.runs / rowCount),
        hitsPerRun: r.hits / r.runs,
        critRate: r.critHits / r.hits,
        averageHit: r.damage / r.hits,
        ...extraOf(extras.get(`${className}\u0000${r.skill}`)),
      }));
    result.push({ className, samples: rowCount, skills });
  }
  return result.sort((a, b) => b.samples - a.samples);
}

/** At least this many chain uses (or rows with a known level) before a rate or an average is shown. */
const MIN_EXTRA_SAMPLES = 10;
const MIN_LEVEL_SAMPLES = 5;

function extraOf(e: { chainOffered: number; chainTaken: number; levelSum: number; levelCount: number } | undefined): { chainRate: number | null; avgLevel: number | null } {
  return {
    chainRate: e && e.chainOffered >= MIN_EXTRA_SAMPLES ? e.chainTaken / e.chainOffered : null,
    avgLevel: e && e.levelCount >= MIN_LEVEL_SAMPLES ? Math.round((e.levelSum / e.levelCount) * 10) / 10 : null,
  };
}
