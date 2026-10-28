import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { eq } from "drizzle-orm";
import { db } from "./db/client.js";
import { bossHpReference } from "./db/schema.js";
import { consistentWithReference, HP_BEFORE_PATCH_2026_10_28, near, type BossHpRef } from "./bossHpRules.js";

export * from "./bossHpRules.js";

/**
 * The real hit points of the bosses, and what they say about an upload.
 *
 * An uploader who joins a fight late sees (say) 40 % of the boss's health as its "maximum"; the damage then adds up to that 40 % and the
 * fight used to pass as a whole kill. The server therefore knows the real health per boss NPC id (table boss_hp_reference: seeded from
 * content/bossHpSeed.json, kept up to date by the spawn values of incoming uploads) and judges the highest reading against it.
 */

const __dirname = path.dirname(fileURLToPath(import.meta.url));
// src/ and dist/ both sit one level below the package root, so one path serves the tsx and the built run.
const SEED_FILE = path.join(__dirname, "..", "src", "content", "bossHpSeed.json");

interface SeedFile {
  npcs?: Record<string, { baseHp?: number; partyScaled?: boolean; source?: string; uploads?: number; spawnFrames?: number }>;
}

/** Inserts the rows of the seed file the table does not have yet; a row that exists (seeded or learned) is never touched. Returns how many were inserted. */
export function seedBossHpReference(file: string = SEED_FILE): number {
  if (!fs.existsSync(file)) {
    return 0;
  }
  const seed = JSON.parse(fs.readFileSync(file, "utf8")) as SeedFile;
  const now = new Date().toISOString();
  let inserted = 0;
  for (const [id, npc] of Object.entries(seed.npcs ?? {})) {
    const npcId = Number(id);
    if (!Number.isInteger(npcId) || npcId <= 0 || !Number.isInteger(npc.baseHp) || (npc.baseHp ?? 0) <= 0) {
      continue;
    }
    const result = db
      .insert(bossHpReference)
      .values({
        npcId,
        baseHp: npc.baseHp!,
        partyScaled: npc.partyScaled === true,
        source: npc.source ?? "seed",
        samples: (npc.spawnFrames ?? 0) + (npc.uploads ?? 0),
        updatedAt: now,
      })
      .onConflictDoNothing()
      .run();
    inserted += result.changes;
  }
  return inserted;
}

export function referenceOf(npcId: number | undefined): BossHpRef | null {
  if (npcId === undefined) {
    return null;
  }
  const row = db.select().from(bossHpReference).where(eq(bossHpReference.npcId, npcId)).get();
  return row ? { baseHp: row.baseHp, partyScaled: row.partyScaled } : null;
}

/**
 * Takes the spawn value of an upload into the reference. No row yet: one report is enough. A row exists: a value that does not fit it
 * (see consistentWithReference) is ignored as suspicious; the same value counts as one more sample; on a boss that is not party-scaled a
 * higher value that fits replaces the row (the reference there is the highest consistent value).
 */
export function learnSpawnHp(npcId: number | undefined, spawnHp: number | undefined, now: string = new Date().toISOString()): void {
  if (npcId === undefined || spawnHp === undefined) {
    return;
  }
  const row = db.select().from(bossHpReference).where(eq(bossHpReference.npcId, npcId)).get();
  if (!row) {
    db.insert(bossHpReference).values({ npcId, baseHp: spawnHp, partyScaled: false, source: "learned", samples: 1, updatedAt: now }).onConflictDoNothing().run();
    return;
  }
  const ref = { baseHp: row.baseHp, partyScaled: row.partyScaled };
  // A late upload of a fight from before the patch must not raise the reduced reference again.
  const before = HP_BEFORE_PATCH_2026_10_28[npcId];
  if (before !== undefined && near(spawnHp, before)) {
    return;
  }
  if (!consistentWithReference(spawnHp, ref)) {
    return;
  }
  if (near(spawnHp, row.baseHp)) {
    db.update(bossHpReference).set({ samples: row.samples + 1, updatedAt: now }).where(eq(bossHpReference.npcId, npcId)).run();
  } else if (!row.partyScaled && spawnHp > row.baseHp) {
    db.update(bossHpReference).set({ baseHp: spawnHp, source: "learned", samples: 1, updatedAt: now }).where(eq(bossHpReference.npcId, npcId)).run();
  }
}

