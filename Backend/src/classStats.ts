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
