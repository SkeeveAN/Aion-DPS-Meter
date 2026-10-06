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

// Rendered preview pictures (seo/ogEncounter.ts). Same rules; a miss that computes null is not remembered.
const bufferEntries = new Map<string, { value: Buffer; expiresAt: number }>();
const MAX_BUFFER_ENTRIES = 300;

export function cachedBuffer(key: string, ttlMs: number, compute: () => Buffer | null): Buffer | null {
  const hit = bufferEntries.get(key);
  const now = Date.now();
  if (hit && hit.expiresAt > now) {
    return hit.value;
  }
  const value = compute();
  if (!value) {
    return null;
  }
  if (bufferEntries.size >= MAX_BUFFER_ENTRIES) {
    bufferEntries.clear();
  }
  bufferEntries.set(key, { value, expiresAt: now + ttlMs });
  return value;
}

export function clearPageCache(): void {
  entries.clear();
  bufferEntries.clear();
}
