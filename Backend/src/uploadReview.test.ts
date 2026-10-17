import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

// DATABASE_PATH must be set before db/client.ts is first imported (see matching/merge.test.ts).
process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-review-test-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { assessKill, structuralProblems, referenceFor, EMPTY_REFERENCE, pendingUploads, approveUpload, rejectUpload } = await import("./uploadReview.js");
const { processUpload } = await import("./matching/merge.js");
const { uploads, encounters } = await import("./db/schema.js");
const { eq } = await import("drizzle-orm");
type Payload = Parameters<typeof processUpload>[0];

function participant(name: string, className: string, damage: number, isSelf = false) {
  return {
    name, className, faction: "Elyos", isSelf, totalDamage: damage, dps: 0, idps: 0, totalHealing: 0, damageTaken: 0, damageAbsorbed: 0, shieldsGiven: [], buffs: [], hps: 0,
    skills: [{ skill: "Cleave", hits: 100, critHits: 20, total: damage, min: 1, max: 100 }], healSkills: [],
  };
}

function payload(overrides: Partial<Payload> = {}, damages: number[] = [600_000, 500_000]): Payload {
  return {
    clientVersion: "test", mode: "", game: "aion2", bossNpcName: "Enhanced Harcon", bossNpcId: 2300104, bossMaxHp: 1_000_000,
    startedAt: "2026-01-01T20:00:00.000Z", endedAt: "2026-01-01T20:03:00.000Z", serverFingerprint: "aion2:europe-kaisinel",
    participants: damages.map((d, i) => participant(`Player${i}`, i % 2 === 0 ? "Gladiator" : "Assassin", d, i === 0)),
    ...overrides,
  } as Payload;
}

const known = { ...EMPTY_REFERENCE, bossKnown: true };

test("dates that cannot be a fight are turned away", () => {
  const now = Date.parse("2026-01-02T00:00:00.000Z");
  assert.deepEqual(structuralProblems(payload(), now), []);
  assert.equal(structuralProblems(payload({ endedAt: "2026-01-01T19:00:00.000Z" }), now)[0]!.code, "end_before_start");
  assert.equal(structuralProblems(payload({ endedAt: "2026-01-01T20:00:01.000Z" }), now)[0]!.code, "too_short");
  assert.equal(structuralProblems(payload({ endedAt: "2026-01-02T08:00:00.000Z" }), now)[0]!.code, "too_long");
  assert.equal(structuralProblems(payload({ startedAt: "2026-01-02T01:00:00.000Z", endedAt: "2026-01-02T01:03:00.000Z" }), now)[0]!.code, "in_the_future");
  assert.equal(structuralProblems(payload({ startedAt: "yesterday" }), now)[0]!.code, "bad_dates");
});

test("a normal kill is not flagged, with or without a reference", () => {
  assert.deepEqual(assessKill(payload(), known), []);
  const reference = { ...known, runs: 5, knownHps: [1_000_000, 2_400_000], clusterRuns: 5, fastestSeconds: 150, classDps: { Gladiator: Array(10).fill(3000) } };
  assert.deepEqual(assessKill(payload(), reference), []);
});

test("more damage than the boss had hit points is flagged, 10 % over is not", () => {
  assert.deepEqual(assessKill(payload({}, [600_000, 500_000]), known), []); // 1.1x
  assert.equal(assessKill(payload({}, [900_000, 900_000]), known)[0]!.code, "damage_over_hp");
});

test("hit points no kill of the boss ever had are flagged, once enough kills are known", () => {
  const few = { ...known, runs: 2, knownHps: [1_000_000] };
  assert.deepEqual(assessKill(payload({ bossMaxHp: 50_000, }, [30_000, 25_000]), few), []);
  const enough = { ...known, runs: 3, knownHps: [1_000_000, 2_400_000] };
  assert.equal(assessKill(payload({ bossMaxHp: 50_000 }, [30_000, 25_000]), enough)[0]!.code, "hp_unknown");
  assert.deepEqual(assessKill(payload({ bossMaxHp: 1_050_000 }, [600_000, 500_000]), enough), []); // within 10 %
});

test("a kill far faster than the fastest known one, and a class far above its median, are flagged", () => {
  const reference = { ...known, runs: 6, knownHps: [1_000_000], clusterRuns: 6, fastestSeconds: 150, classDps: { Gladiator: Array(10).fill(2000), Assassin: Array(10).fill(2000) } };
  const codes = assessKill(payload({ endedAt: "2026-01-01T20:01:00.000Z" }, [600_000, 500_000]), reference).map((f) => f.code);
  assert.ok(codes.includes("too_fast"));
  assert.ok(codes.filter((c) => c === "class_dps_outlier").length >= 1);
  assert.deepEqual(assessKill(payload({ endedAt: "2026-01-01T20:02:30.000Z" }, [300_000, 300_000]), reference), []);
});

test("an uncatalogued boss is flagged", () => {
  assert.equal(assessKill(payload(), EMPTY_REFERENCE)[0]!.code, "unknown_boss");
});

test("the reference comes from the kills on file; a flagged upload waits and is merged only when approved", () => {
  // three ordinary kills of the boss, 150 s each, 1,000,000 hit points
  for (let i = 0; i < 3; i++) {
    processUpload(payload({ startedAt: `2026-01-0${i + 1}T20:00:00.000Z`, endedAt: `2026-01-0${i + 1}T20:02:30.000Z` }, [600_000, 500_000]));
  }

  const reference = referenceFor(payload());
  assert.equal(reference.bossKnown, true);
  assert.equal(reference.runs, 3);
  assert.equal(reference.clusterRuns, 3);
  assert.equal(reference.fastestSeconds, 150);
  assert.ok((reference.classDps.Gladiator?.length ?? 0) >= 3);

  const odd = payload({ startedAt: "2026-02-01T20:00:00.000Z", endedAt: "2026-02-01T20:00:20.000Z" }, [600_000, 500_000]);
  const findings = assessKill(odd, referenceFor(odd));
  assert.ok(findings.some((f) => f.code === "too_fast"));

  db.insert(uploads).values({ clientVersion: "test", uploaderReportedName: "Player0", ipHash: "x", status: "pending", receivedAt: "2026-02-01 20:01:00", reviewReason: JSON.stringify(findings), rawPayloadJson: JSON.stringify(odd) }).run();
  const before = db.select().from(encounters).all().length;
  const waiting = pendingUploads();
  assert.equal(waiting.length, 1);
  assert.equal(waiting[0]!.boss, "Enhanced Harcon");
  assert.ok(waiting[0]!.reasons.some((r) => r.code === "too_fast"));
  assert.equal(db.select().from(encounters).all().length, before); // still off the leaderboards

  const approved = approveUpload(waiting[0]!.id);
  assert.ok(approved);
  assert.equal(db.select().from(encounters).all().length, before + 1);
  assert.equal(pendingUploads().length, 0);
  assert.equal(db.select().from(uploads).where(eq(uploads.id, waiting[0]!.id)).get()!.status, "merged");
  // the fight is dated by when it was uploaded, not by when the review let it through
  assert.equal(db.select().from(encounters).where(eq(encounters.id, approved.encounterId)).get()!.createdAt, "2026-02-01 20:01:00");
  assert.equal(approveUpload(waiting[0]!.id), null); // only once

  db.insert(uploads).values({ clientVersion: "test", uploaderReportedName: "Player0", ipHash: "x", status: "pending", reviewReason: "[]", rawPayloadJson: JSON.stringify(odd) }).run();
  const second = pendingUploads()[0]!;
  assert.equal(rejectUpload(second.id), true);
  assert.equal(rejectUpload(second.id), false);
  assert.equal(pendingUploads().length, 0);
});
