import { createHash, randomBytes } from "node:crypto";
import { and, eq, isNull } from "drizzle-orm";
import { db } from "./db/client.js";
import { apiKeys } from "./db/schema.js";

// Keys of the partner API (partnerApi.ts). A key is "adps_" and 32 random bytes; only its SHA-256 is stored, so a leaked database gives no key.

export const KEY_HEADER = "x-api-key";
export const DEFAULT_RATE_PER_MINUTE = 60;

export function hashKey(key: string): string {
  return createHash("sha256").update(key).digest("hex");
}

/** Makes a key for a partner; the key is returned here and never again. */
export function createKey(name: string, ratePerMinute = DEFAULT_RATE_PER_MINUTE): { id: number; key: string } {
  const key = `adps_${randomBytes(32).toString("base64url")}`;
  const row = db.insert(apiKeys).values({ name, keyHash: hashKey(key), ratePerMinute }).returning({ id: apiKeys.id }).get();
  return { id: row.id, key };
}

export function listKeys() {
  return db.select({ id: apiKeys.id, name: apiKeys.name, ratePerMinute: apiKeys.ratePerMinute, createdAt: apiKeys.createdAt, lastUsedAt: apiKeys.lastUsedAt, revokedAt: apiKeys.revokedAt }).from(apiKeys).all();
}

export function revokeKey(id: number): boolean {
  return db.update(apiKeys).set({ revokedAt: new Date().toISOString() }).where(and(eq(apiKeys.id, id), isNull(apiKeys.revokedAt))).run().changes > 0;
}

export type PartnerKey = { id: number; name: string; ratePerMinute: number };

/** The key behind a given secret, null for an unknown or revoked one. */
export function findKey(secret: string): PartnerKey | null {
  const row = db.select({ id: apiKeys.id, name: apiKeys.name, ratePerMinute: apiKeys.ratePerMinute }).from(apiKeys).where(and(eq(apiKeys.keyHash, hashKey(secret)), isNull(apiKeys.revokedAt))).get();
  return row ?? null;
}

const lastTouched = new Map<number, number>();
/** Notes that a key was used; the row is written at most once a minute per key. */
export function touchKey(id: number, now = Date.now()): void {
  if (now - (lastTouched.get(id) ?? 0) < 60_000) {
    return;
  }
  lastTouched.set(id, now);
  db.update(apiKeys).set({ lastUsedAt: new Date(now).toISOString() }).where(eq(apiKeys.id, id)).run();
}

const windows = new Map<number, { start: number; count: number }>();
/** Fixed one-minute window per key. Returns the seconds to wait when the key is over its limit, 0 when the request may go on. */
export function rateCheck(key: PartnerKey, now = Date.now()): number {
  const w = windows.get(key.id);
  if (!w || now - w.start >= 60_000) {
    windows.set(key.id, { start: now, count: 1 });
    return 0;
  }
  if (w.count >= key.ratePerMinute) {
    return Math.max(1, Math.ceil((w.start + 60_000 - now) / 1000));
  }
  w.count++;
  return 0;
}

export function resetRateLimits(): void {
  windows.clear();
  lastTouched.clear();
}
