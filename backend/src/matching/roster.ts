/** Trims and case-folds a player name for comparison/lookup, never for display. */
export function normalizeName(name: string): string {
  return name.trim().toLowerCase();
}

/** Size of the intersection over the size of the union, 1 for two empty sets (nothing to disagree on). */
export function jaccardSimilarity(a: ReadonlySet<string>, b: ReadonlySet<string>): number {
  if (a.size === 0 && b.size === 0) {
    return 1;
  }

  let intersection = 0;
  for (const item of a) {
    if (b.has(item)) {
      intersection++;
    }
  }

  const union = a.size + b.size - intersection;
  return union === 0 ? 0 : intersection / union;
}

/** True if two values are within `toleranceRatio` of the larger one (both non-negative). */
export function withinRelativeTolerance(a: number, b: number, toleranceRatio: number): boolean {
  const larger = Math.max(Math.abs(a), Math.abs(b));
  if (larger === 0) {
    return true;
  }
  return Math.abs(a - b) / larger <= toleranceRatio;
}
