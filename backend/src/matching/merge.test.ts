import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

// DATABASE_PATH must be set before db/client.ts is first imported (it opens
// the file at module load time), so this has to run before any other import
// in this file pulls that module in transitively.
process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-test-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db } = await import("../db/client.js");
const migrationsFolder = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "drizzle");
migrate(db, { migrationsFolder });

const { processUpload } = await import("./merge.js");
const { encounterParticipants, players } = await import("../db/schema.js");
const { eq } = await import("drizzle-orm");

function basePayload(overrides: Partial<Parameters<typeof processUpload>[0]> = {}) {
  return {
    clientVersion: "test",
    bossNpcName: "Raksha Kochherz",
    startedAt: "2026-01-01T20:00:00.000Z",
    endedAt: "2026-01-01T20:03:12.000Z",
    participants: [
      {
        name: "Anna",
        className: "Gladiator",
        faction: "Elyos",
        isSelf: true,
        totalDamage: 800_000,
        dps: 4100,
        idps: 4100,
        skills: [{ skill: "Skyfall", hits: 100, critHits: 20, total: 800_000, min: 2000, max: 12_000 }],
      },
      {
        name: "Bob",
        className: "Assassine",
        faction: "Elyos",
        isSelf: false,
        totalDamage: 900_000,
        dps: 4600,
        idps: 4600,
        skills: [{ skill: "Stab", hits: 150, critHits: 10, total: 900_000, min: 1000, max: 9000 }],
      },
    ],
    ...overrides,
  };
}

test("two uploads of the same fight merge into one encounter, not two", () => {
  const first = processUpload(basePayload());
  assert.equal(first.status, "created");

  const second = processUpload(
    basePayload({
      startedAt: "2026-01-01T20:00:05.000Z",
      endedAt: "2026-01-01T20:03:10.000Z",
    }),
  );
  assert.equal(second.status, "merged");
  assert.equal(second.encounterId, first.encounterId);
});

test("worst case: uploader's own name is wrong, gets corrected from a teammate's earlier upload", () => {
  const first = processUpload(basePayload());

  const second = processUpload(
    basePayload({
      startedAt: "2026-01-01T20:00:03.000Z",
      endedAt: "2026-01-01T20:03:09.000Z",
      participants: [
        // Bob's own client has "XxSlayerxX" misconfigured as his character name.
        {
          name: "Anna",
          className: "Gladiator",
          faction: "Elyos",
          isSelf: false,
          totalDamage: 800_000,
          dps: 4100,
          idps: 4100,
          skills: [{ skill: "Skyfall", hits: 100, critHits: 9, total: 800_000, min: 2000, max: 12_000 }],
        },
        {
          name: "XxSlayerxX",
          className: "Assassine",
          faction: "Elyos",
          isSelf: true,
          totalDamage: 905_000,
          dps: 4620,
          idps: 4620,
          skills: [{ skill: "Stab", hits: 150, critHits: 32, total: 905_000, min: 1000, max: 9000 }],
        },
      ],
    }),
  );

  assert.equal(second.status, "merged");
  assert.equal(second.encounterId, first.encounterId);

  const rows = db
    .select({ name: players.name, crit: encounterParticipants.critRatePercent, authoritative: encounterParticipants.isCritRateAuthoritative })
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .where(eq(encounterParticipants.encounterId, first.encounterId))
    .all();

  assert.equal(rows.length, 2, "no duplicate row should have been created for the renamed player");

  const renamed = rows.find((r) => r.name === "XxSlayerxX");
  assert.ok(renamed, "the Bob row should now be named XxSlayerxX");
  assert.equal(renamed!.authoritative, true);
  assert.ok(Math.abs(renamed!.crit - (100 * 32) / 150) < 0.01, "crit rate should come from the self-report");

  const anna = rows.find((r) => r.name === "Anna");
  assert.equal(anna!.crit, 20, "Anna's own authoritative crit rate must not be overwritten by a third party's guess");
});

test("a different boss never merges into an unrelated encounter", () => {
  const first = processUpload(basePayload());
  const second = processUpload(basePayload({ bossNpcName: "Some Other Boss" }));
  assert.equal(second.status, "created");
  assert.notEqual(second.encounterId, first.encounterId);
});

test("a solo upload with no visible teammates still merges with a later, fuller upload of the same fight", () => {
  const solo = processUpload(
    basePayload({
      participants: [
        {
          name: "Hidan",
          className: "Assassine",
          faction: "Elyos",
          isSelf: true,
          totalDamage: 800_000,
          dps: 4100,
          idps: 4100,
          skills: [{ skill: "Skyfall", hits: 100, critHits: 20, total: 800_000, min: 2000, max: 12_000 }],
        },
      ],
    }),
  );
  assert.equal(solo.status, "created");

  const fuller = processUpload(
    basePayload({
      participants: [
        {
          name: "Hidan",
          className: "Assassine",
          faction: "Elyos",
          isSelf: false,
          totalDamage: 800_000,
          dps: 4100,
          idps: 4100,
          skills: [{ skill: "Skyfall", hits: 100, critHits: 9, total: 800_000, min: 2000, max: 12_000 }],
        },
        {
          name: "Anna",
          className: "Gladiator",
          faction: "Elyos",
          isSelf: true,
          totalDamage: 700_000,
          dps: 3900,
          idps: 3900,
          skills: [{ skill: "Skyfall", hits: 90, critHits: 15, total: 700_000, min: 1500, max: 11_000 }],
        },
      ],
    }),
  );

  assert.equal(fuller.status, "merged");
  assert.equal(fuller.encounterId, solo.encounterId);
});

test("same boss but far apart in time creates a separate encounter", () => {
  const first = processUpload(basePayload());
  const second = processUpload(
    basePayload({ startedAt: "2026-01-01T21:00:00.000Z", endedAt: "2026-01-01T21:03:12.000Z" }),
  );
  assert.equal(second.status, "created");
  assert.notEqual(second.encounterId, first.encounterId);
});
