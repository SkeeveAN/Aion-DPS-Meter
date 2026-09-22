// In-memory cache for rendered page shells and the sitemap. Small site, single process: a Map with
// per-entry expiry is all that's needed. Cleared wholesale after every accepted upload, so a fresh
// leaderboard row never waits out a TTL.
const entries = new Map<string, { value: string; expiresAt: number }>();
const MAX_ENTRIES = 2000;

export function cached(key: string, ttlMs: number, compute: () => string): string {
  const hit = entries.get(key);
  const now = Date.now();
  if (hit && hit.expiresAt > now) {
    return hit.value;
  }
  const value = compute();
  if (entries.size >= MAX_ENTRIES) {
    entries.clear();
  }
  entries.set(key, { value, expiresAt: now + ttlMs });
  return value;
}

export function clearPageCache(): void {
  entries.clear();
}
