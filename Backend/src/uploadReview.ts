import { and, between, eq, isNotNull } from "drizzle-orm";
import { db } from "./db/client.js";
import { encounterParticipants, encounters, uploads } from "./db/schema.js";
import {
  CLASS_DPS_FACTOR,
  FASTEST_KILL_FACTOR,
  HP_REFERENCE_TOLERANCE,
  MAX_KILL_DAMAGE_SHARE,
  MAX_KILL_SECONDS,
  MIN_CLASS_SAMPLES,
  MIN_KILL_SECONDS,
  MIN_REFERENCE_RUNS,
} from "./constants.js";
import { findBossId, processUpload } from "./matching/merge.js";
import { uploadSchema, type UploadPayload } from "./uploadSchema.js";
import { clearPageCache } from "./seo/cache.js";

/** One thing about an upload that does not look right: a short code and the numbers behind it. */
export interface ReviewFinding {
  code: string;
  detail: string;
}

/** What other kills of the same boss say (see {@link referenceFor}); every field is empty when nothing is known. */
export interface ReviewReference {
  /** Kills of this boss on file, whatever their hit points. */
  runs: number;
  /** The distinct hit-point values of this boss on file (a boss has one per difficulty). */
  knownHps: number[];
  /** Kills with hit points like the upload's: their count, the fastest of them (seconds) and per class the damage per second of everyone who took part. */
  clusterRuns: number;
  fastestSeconds: number | null;
  classDps: Record<string, number[]>;
  /** The boss is on file at all. */
  bossKnown: boolean;
}

export const EMPTY_REFERENCE: ReviewReference = { runs: 0, knownHps: [], clusterRuns: 0, fastestSeconds: null, classDps: {}, bossKnown: false };

export function durationSeconds(payload: Pick<UploadPayload, "startedAt" | "endedAt">): number {
  return (Date.parse(payload.endedAt) - Date.parse(payload.startedAt)) / 1000;
}

function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid]! : (sorted[mid - 1]! + sorted[mid]!) / 2;
}

/**
 * What cannot be a real fight whatever other kills say: the dates do not read, the end is not after the start, the start lies in the future, or it lasted no time or longer than any raid.
 * These uploads are turned away (400); they are never worth a look.
 */
export function structuralProblems(payload: UploadPayload, now: number): ReviewFinding[] {
  const start = Date.parse(payload.startedAt);
  const end = Date.parse(payload.endedAt);
  if (!Number.isFinite(start) || !Number.isFinite(end)) {
    return [{ code: "bad_dates", detail: `${payload.startedAt} / ${payload.endedAt}` }];
  }

  const problems: ReviewFinding[] = [];
  const seconds = (end - start) / 1000;
  if (end <= start) {
    problems.push({ code: "end_before_start", detail: `${seconds} s` });
  } else if (seconds < MIN_KILL_SECONDS) {
    problems.push({ code: "too_short", detail: `${seconds} s` });
  } else if (seconds > MAX_KILL_SECONDS) {
    problems.push({ code: "too_long", detail: `${Math.round(seconds)} s` });
  }

  if (start > now + 10 * 60 * 1000) {
    problems.push({ code: "in_the_future", detail: payload.startedAt });
  }

  return problems;
}

/**
 * What looks wrong about a kill that is formally fine: more damage than the boss had hit points, hit points no kill of this boss ever had, a kill much faster than the fastest on file,
 * players doing far more than their class does on this boss, a boss nobody has catalogued. Judged by comparison only when enough kills are known ({@link MIN_REFERENCE_RUNS}): a new boss
 * must be able to get its first ones. Pure, so a test can run it.
 */
export function assessKill(payload: UploadPayload, reference: ReviewReference): ReviewFinding[] {
  const findings: ReviewFinding[] = [];
  const seconds = Math.max(durationSeconds(payload), 0.001);
  const groupDamage = payload.participants.reduce((sum, p) => sum + p.totalDamage, 0);
  const hp = payload.bossMaxHp;
  // Estimated damage (hit counts x typical hit) says nothing about plausibility: the damage checks are skipped for such uploads.
  const hasEstimatedRows = payload.participants.some((p) => p.estimated);

  if (!reference.bossKnown) {
    findings.push({ code: "unknown_boss", detail: `${payload.bossNpcName}${payload.bossNpcId !== undefined ? ` (${payload.bossNpcId})` : ""}` });
  }

  if (!hasEstimatedRows && hp !== undefined && groupDamage > hp * MAX_KILL_DAMAGE_SHARE) {
    findings.push({ code: "damage_over_hp", detail: `${groupDamage} damage on ${hp} hit points (${(groupDamage / hp).toFixed(2)}x)` });
  }

  if (hp !== undefined && reference.runs >= MIN_REFERENCE_RUNS && reference.knownHps.length > 0) {
    const known = reference.knownHps.some((v) => Math.abs(hp / v - 1) <= HP_REFERENCE_TOLERANCE);
    if (!known) {
      findings.push({ code: "hp_unknown", detail: `${hp} hit points; known: ${reference.knownHps.slice(0, 6).join(", ")}` });
    }
  }

  if (reference.clusterRuns >= MIN_REFERENCE_RUNS && reference.fastestSeconds !== null && seconds < reference.fastestSeconds * FASTEST_KILL_FACTOR) {
    findings.push({ code: "too_fast", detail: `${seconds.toFixed(1)} s against the fastest known ${reference.fastestSeconds.toFixed(1)} s` });
  }

  if (reference.clusterRuns >= MIN_REFERENCE_RUNS) {
    for (const p of payload.participants) {
      if (p.estimated) {
        continue;
      }

      const samples = reference.classDps[p.className];
      if (!samples || samples.length < MIN_CLASS_SAMPLES) {
        continue;
      }

      const dps = p.totalDamage / seconds;
      const typical = median(samples);
      if (typical > 0 && dps > typical * CLASS_DPS_FACTOR) {
        findings.push({ code: "class_dps_outlier", detail: `${p.className} ${Math.round(dps)} damage per second, the class's median is ${Math.round(typical)}` });
      }
    }
  }

  return findings;
}

/** What the database knows about kills like this one: the same boss, and among them those with hit points like the upload's. Reads only. */
export function referenceFor(payload: UploadPayload): ReviewReference {
  const bossId = findBossId(payload);
  if (bossId === null) {
    return EMPTY_REFERENCE;
  }

  const rows = db
    .select({ id: encounters.id, hp: encounters.bossMaxHp, seconds: encounters.durationSeconds })
    .from(encounters)
    // A partial fight (a late joiner's) is no reference for what a kill looks like.
    .where(and(eq(encounters.bossId, bossId), eq(encounters.isPartial, false)))
    .all();
  const knownHps = [...new Set(rows.map((r) => r.hp).filter((v): v is number => v !== null))].sort((a, b) => a - b);
  const hp = payload.bossMaxHp;
  const cluster = hp === undefined ? [] : rows.filter((r) => r.hp !== null && Math.abs(r.hp / hp - 1) <= HP_REFERENCE_TOLERANCE);
  const classDps: Record<string, number[]> = {};
  if (cluster.length > 0 && hp !== undefined) {
    const people = db
      .select({ className: encounterParticipants.className, damage: encounterParticipants.totalDamage, seconds: encounters.durationSeconds })
      .from(encounterParticipants)
      .innerJoin(encounters, eq(encounters.id, encounterParticipants.encounterId))
      .where(and(eq(encounters.bossId, bossId), eq(encounters.isPartial, false), eq(encounterParticipants.estimated, false), isNotNull(encounters.bossMaxHp), between(encounters.bossMaxHp, Math.floor(hp * (1 - HP_REFERENCE_TOLERANCE)), Math.ceil(hp * (1 + HP_REFERENCE_TOLERANCE)))))
      .all();
    for (const person of people) {
      if (person.seconds > 0 && person.damage > 0) {
        (classDps[person.className] ??= []).push(person.damage / person.seconds);
      }
    }
  }

  return {
    runs: rows.length,
    knownHps,
    clusterRuns: cluster.length,
    fastestSeconds: cluster.length > 0 ? Math.min(...cluster.map((r) => r.seconds)) : null,
    classDps,
    bossKnown: true,
  };
}

// ---- the review queue ---------------------------------------------------------------------------------------------

export interface PendingUpload {
  id: number;
  receivedAt: string;
  clientVersion: string;
  boss: string;
  bossMaxHp: number | null;
  groupDamage: number;
  seconds: number;
  participants: number;
  reasons: ReviewFinding[];
}

function parseReasons(raw: string | null): ReviewFinding[] {
  try {
    const value = raw ? (JSON.parse(raw) as unknown) : [];
    return Array.isArray(value) ? (value as ReviewFinding[]) : [];
  } catch {
    return [];
  }
}

/** The uploads waiting for a decision, oldest first. */
export function pendingUploads(limit = 100): PendingUpload[] {
  const rows = db.select().from(uploads).where(eq(uploads.status, "pending")).orderBy(uploads.id).limit(limit).all();
  const result: PendingUpload[] = [];
  for (const row of rows) {
    let payload: UploadPayload | null = null;
    try {
      const parsed = uploadSchema.safeParse(JSON.parse(row.rawPayloadJson));
      payload = parsed.success ? parsed.data : null;
    } catch {
      payload = null;
    }

    result.push({
      id: row.id,
      receivedAt: row.receivedAt,
      clientVersion: row.clientVersion,
      boss: payload?.bossNpcName ?? "?",
      bossMaxHp: payload?.bossMaxHp ?? null,
      groupDamage: payload?.participants.reduce((sum, p) => sum + p.totalDamage, 0) ?? 0,
      seconds: payload ? durationSeconds(payload) : 0,
      participants: payload?.participants.length ?? 0,
      reasons: parseReasons(row.reviewReason),
    });
  }

  return result;
}

/** Puts a waiting upload through the ordinary merge, as if it had just come in; false when there is none waiting under that id. */
export function approveUpload(id: number): { encounterId: number } | null {
  const row = db.select().from(uploads).where(and(eq(uploads.id, id), eq(uploads.status, "pending"))).get();
  if (!row) {
    return null;
  }

  const parsed = uploadSchema.safeParse(JSON.parse(row.rawPayloadJson));
  if (!parsed.success) {
    throw new Error("the stored payload no longer reads: " + parsed.error.message);
  }

  const result = processUpload(parsed.data, row.receivedAt);
  db.update(uploads)
    .set({ status: "merged", matchedEncounterId: result.encounterId, serverId: result.serverId, reviewedAt: new Date().toISOString() })
    .where(eq(uploads.id, id))
    .run();
  clearPageCache();
  return { encounterId: result.encounterId };
}

export function rejectUpload(id: number): boolean {
  const done = db.update(uploads).set({ status: "rejected", reviewedAt: new Date().toISOString() }).where(and(eq(uploads.id, id), eq(uploads.status, "pending"))).run();
  return done.changes > 0;
}
