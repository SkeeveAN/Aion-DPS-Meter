import { after, test } from "node:test";
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
const { db, sqlite } = await import("../db/client.js");
const migrationsFolder = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "drizzle");
migrate(db, { migrationsFolder });

// Finalize every prepared statement before Node tears the environment down (see db/client.ts).
after(() => sqlite.close());

const { processUpload } = await import("./merge.js");
const { bossNpcIds, bosses, encounterParticipants, encounterSkillUsage, encounters, instances, players } = await import("../db/schema.js");
const { and, eq } = await import("drizzle-orm");

function basePayload(overrides: Partial<Parameters<typeof processUpload>[0]> = {}) {
  return {
    clientVersion: "test",
    mode: "",
    game: "aion2" as const,
    bossNpcName: "Enhanced Harcon",
    startedAt: "2026-01-01T20:00:00.000Z",
    endedAt: "2026-01-01T20:03:12.000Z",
    serverFingerprint: "aion2:europe-kaisinel",
    participants: [
      {
        name: "Anna",
        className: "Gladiator",
        faction: "Elyos",
        isSelf: true,
        totalDamage: 800_000,
        dps: 4100,
        idps: 4100,
        totalHealing: 0,
        damageTaken: 0,
        damageAbsorbed: 0,
        shieldsGiven: [],
        buffs: [],
        hps: 0,
        skills: [{ skill: "Rending Blow", hits: 100, critHits: 20, total: 800_000, min: 2000, max: 12_000 }],
        healSkills: [],
      },
      {
        name: "Bob",
        className: "Assassin",
        faction: "Elyos",
        isSelf: false,
        totalDamage: 900_000,
        dps: 4600,
        idps: 4600,
        totalHealing: 0,
        damageTaken: 0,
        damageAbsorbed: 0,
        shieldsGiven: [],
        buffs: [],
        hps: 0,
        skills: [{ skill: "Spinning Strike", hits: 150, critHits: 10, total: 900_000, min: 1000, max: 9000 }],
        healSkills: [],
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
          totalHealing: 0,
          damageTaken: 0,
          damageAbsorbed: 0,
          shieldsGiven: [],
          buffs: [],
          hps: 0,
          skills: [{ skill: "Rending Blow", hits: 100, critHits: 9, total: 800_000, min: 2000, max: 12_000 }],
          healSkills: [],
        },
        {
          name: "XxSlayerxX",
          className: "Assassin",
          faction: "Elyos",
          isSelf: true,
          totalDamage: 905_000,
          dps: 4620,
          idps: 4620,
          totalHealing: 0,
          damageTaken: 0,
          damageAbsorbed: 0,
          shieldsGiven: [],
          buffs: [],
          hps: 0,
          skills: [{ skill: "Spinning Strike", hits: 150, critHits: 32, total: 905_000, min: 1000, max: 9000 }],
          healSkills: [],
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
          className: "Assassin",
          faction: "Elyos",
          isSelf: true,
          totalDamage: 800_000,
          dps: 4100,
          idps: 4100,
          totalHealing: 0,
          damageTaken: 0,
          damageAbsorbed: 0,
          shieldsGiven: [],
          buffs: [],
          hps: 0,
          skills: [{ skill: "Rending Blow", hits: 100, critHits: 20, total: 800_000, min: 2000, max: 12_000 }],
          healSkills: [],
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
          className: "Assassin",
          faction: "Elyos",
          isSelf: false,
          totalDamage: 800_000,
          dps: 4100,
          idps: 4100,
          totalHealing: 0,
          damageTaken: 0,
          damageAbsorbed: 0,
          shieldsGiven: [],
          buffs: [],
          hps: 0,
          skills: [{ skill: "Rending Blow", hits: 100, critHits: 9, total: 800_000, min: 2000, max: 12_000 }],
          healSkills: [],
        },
        {
          name: "Anna",
          className: "Gladiator",
          faction: "Elyos",
          isSelf: true,
          totalDamage: 700_000,
          dps: 3900,
          idps: 3900,
          totalHealing: 0,
          damageTaken: 0,
          damageAbsorbed: 0,
          shieldsGiven: [],
          buffs: [],
          hps: 0,
          skills: [{ skill: "Rending Blow", hits: 90, critHits: 15, total: 700_000, min: 1500, max: 11_000 }],
          healSkills: [],
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

test("an identical fight uploaded from a different server never merges - gear standards aren't comparable", () => {
  const first = processUpload(basePayload());
  const second = processUpload(basePayload({ serverFingerprint: "222.231.10.116:10241" }));
  assert.equal(second.status, "created");
  assert.notEqual(second.encounterId, first.encounterId);
  assert.notEqual(second.serverId, first.serverId);
});

test("the same player name on two different servers is tracked as two separate players", () => {
  const first = processUpload(basePayload());
  const second = processUpload(basePayload({ serverFingerprint: "222.231.10.116:10241" }));

  const annaOnFirstServer = db
    .select({ id: players.id })
    .from(players)
    .where(and(eq(players.serverId, first.serverId), eq(players.nameNormalized, "anna")))
    .get();
  const annaOnSecondServer = db
    .select({ id: players.id })
    .from(players)
    .where(and(eq(players.serverId, second.serverId), eq(players.nameNormalized, "anna")))
    .get();

  assert.ok(annaOnFirstServer, "Anna should exist on the first server");
  assert.ok(annaOnSecondServer, "Anna should exist on the second server");
  assert.notEqual(annaOnFirstServer!.id, annaOnSecondServer!.id, "the two Annas must be different player rows");
});

test("an Aion 2 upload with a known bossNpcId lands on that boss even when the name differs", () => {
  const seeded = processUpload(
    basePayload({
      game: "aion2",
      bossNpcName: "Ultimate Berk",
      serverFingerprint: "aion2:10.0.0.1:7777",
      serverName: "Aion 2 EU",
      participants: basePayload().participants.map((p) => ({ ...p, className: "Templar" })),
    }),
  );
  const seededBossId = db.select({ bossId: encounters.bossId }).from(encounters).where(eq(encounters.id, seeded.encounterId)).get()!.bossId;
  db.insert(bossNpcIds).values({ bossId: seededBossId, npcId: 2300171 }).run();

  const byId = processUpload(
    basePayload({
      game: "aion2",
      bossNpcName: "Ultimate Berk (Hard)",
      bossNpcId: 2300171,
      serverFingerprint: "aion2:10.0.0.1:7777",
      serverName: "Aion 2 EU",
      startedAt: "2026-02-01T20:00:00.000Z",
      endedAt: "2026-02-01T20:04:00.000Z",
      participants: basePayload().participants.map((p) => ({ ...p, className: "Templar" })),
    }),
  );
  const resolvedBossId = db.select({ bossId: encounters.bossId }).from(encounters).where(eq(encounters.id, byId.encounterId)).get()!.bossId;
  assert.equal(resolvedBossId, seededBossId);
});

test("a pure healer with zero boss damage still gets an encounter row - not just damage dealers", () => {
  const result = processUpload(
    basePayload({
      participants: [
        {
          name: "Healmimi",
          className: "Cleric",
          faction: "Elyos",
          isSelf: true,
          totalDamage: 0,
          dps: 0,
          idps: 0,
          totalHealing: 300_000,
          damageTaken: 0,
          damageAbsorbed: 0,
          shieldsGiven: [],
          buffs: [],
          hps: 1666.7,
          skills: [],
          healSkills: [
            { skill: "Healing Light V", hits: 40, critHits: 3, total: 300_000, min: 5000, max: 12_000 },
          ],
        },
      ],
    }),
  );

  const row = db
    .select({
      totalDamage: encounterParticipants.totalDamage,
      totalHealing: encounterParticipants.totalHealing,
      hps: encounterParticipants.hps,
      id: encounterParticipants.id,
    })
    .from(encounterParticipants)
    .where(eq(encounterParticipants.encounterId, result.encounterId))
    .get()!;

  assert.equal(row.totalDamage, 0);
  assert.equal(row.totalHealing, 300_000);
  assert.ok(Math.abs(row.hps - 1666.7) < 0.1);

  const skillRow = db
    .select({ skillName: encounterSkillUsage.skillName, isHeal: encounterSkillUsage.isHeal })
    .from(encounterSkillUsage)
    .where(eq(encounterSkillUsage.participantId, row.id))
    .get()!;
  assert.equal(skillRow.skillName, "Healing Light V");
  assert.equal(skillRow.isHeal, true);
});

// --- players an old client filed under the uploader's server move to the server a newer upload names (rehome.ts) ---

function member(name: string, faction: string, extra: { guild?: string; serverId?: number } = {}) {
  const base = basePayload().participants[1];
  return { ...base, name, faction, isSelf: false, ...extra };
}

let fightNo = 0;
function upload(participants: ReturnType<typeof member>[], withServers: boolean, serverName = "Europe - Rehome", uploaderFingerprint = "aion2:europe-rehome") {
  fightNo++;
  const start = new Date(Date.UTC(2026, 2, 1, 10, fightNo * 10)).toISOString();
  return processUpload(
    basePayload({
      serverFingerprint: uploaderFingerprint,
      serverName,
      startedAt: start,
      endedAt: new Date(Date.parse(start) + 120_000).toISOString(),
      participants: participants.map((p) => (withServers ? p : { ...p, serverId: undefined })),
    }),
  );
}

const rowCount = (name: string) => (sqlite.prepare("select count(*) n from players where name_normalized = ?").get(name.toLowerCase()) as { n: number }).n;

test("an Asmodian filed under an Elyos server moves to the server he names now - same row, no second one", () => {
  // Europe - Kaisinel is an Elyos server: Mira (Asmodian) cannot really be there.
  upload([member("Mira", "Asmodian")], false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  const before = sqlite.prepare("select id from players where name_normalized = 'mira'").get() as { id: number };
  upload([member("Mira", "Asmodian", { serverId: 2308 })], true, "Europe - Kaisinel", "aion2:europe-kaisinel");
  const after = sqlite.prepare("select p.id id, s.display_name d from players p join servers s on s.id = p.server_id where p.name_normalized = 'mira'").all() as { id: number; d: string }[];
  assert.equal(after.length, 1, "no second Mira");
  assert.equal(after[0].id, before.id);
  assert.equal(after[0].d, "Europe - Beritra");
});

test("the same legion on both sides moves an Elyos to the server he names", () => {
  upload([member("Odin", "Elyos", { guild: "Nordwind" })], false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  upload([member("Odin", "Elyos", { guild: "Nordwind", serverId: 1305 })], true, "Europe - Kaisinel", "aion2:europe-kaisinel");
  assert.equal(rowCount("odin"), 1);
  const d = (sqlite.prepare("select s.display_name d from players p join servers s on s.id = p.server_id where p.name_normalized = 'odin'").get() as { d: string }).d;
  assert.equal(d, "Europe - Yustiel");
});

test("an Elyos without a legion match stays where he was - a namesake of another server must not take the row", () => {
  upload([member("Thora", "Elyos")], false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  upload([member("Thora", "Elyos", { serverId: 1305 })], true, "Europe - Kaisinel", "aion2:europe-kaisinel");
  assert.equal(rowCount("thora"), 2, "a second row for the other server, the old one untouched");
  const old = sqlite.prepare("select s.display_name d from players p join servers s on s.id = p.server_id where p.name_normalized = 'thora' order by p.id limit 1").get() as { d: string };
  assert.equal(old.d, "Europe - Kaisinel");
});

test("a different legion never moves a row", () => {
  upload([member("Freya", "Elyos", { guild: "Alpha" })], false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  upload([member("Freya", "Elyos", { guild: "Beta", serverId: 1305 })], true, "Europe - Kaisinel", "aion2:europe-kaisinel");
  assert.equal(rowCount("freya"), 2);
});

test("three players seen together twice before are recognised through each other, even when the new upload names no legion", () => {
  const crew = ["Anke", "Bernd", "Cora"];
  // Two earlier fights from an old client, the legion known.
  upload(crew.map((n) => member(n, "Elyos", { guild: "Eisenfaust" })), false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  upload(crew.map((n) => member(n, "Elyos", { guild: "Eisenfaust" })), false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  // Anke and Bernd are named with a legion (rule 2); Cora comes without one and is identified by them (rule 3).
  upload(
    [member("Anke", "Elyos", { guild: "Eisenfaust", serverId: 1305 }), member("Bernd", "Elyos", { guild: "Eisenfaust", serverId: 1305 }), member("Cora", "Elyos", { serverId: 1305 })],
    true,
    "Europe - Kaisinel",
    "aion2:europe-kaisinel",
  );
  for (const n of crew) {
    assert.equal(rowCount(n), 1, `${n} keeps his single row`);
    const d = (sqlite.prepare("select s.display_name d from players p join servers s on s.id = p.server_id where p.name_normalized = ?").get(n.toLowerCase()) as { d: string }).d;
    assert.equal(d, "Europe - Yustiel", n);
  }
});

test("one mate is not enough for the group rule", () => {
  const crew = ["Dirk", "Edda"];
  upload(crew.map((n) => member(n, "Elyos", { guild: "Zweier" })), false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  upload(crew.map((n) => member(n, "Elyos", { guild: "Zweier" })), false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  upload([member("Dirk", "Elyos", { guild: "Zweier", serverId: 1305 }), member("Edda", "Elyos", { serverId: 1305 })], true, "Europe - Kaisinel", "aion2:europe-kaisinel");
  assert.equal(rowCount("dirk"), 1, "Dirk moved by his legion");
  assert.equal(rowCount("edda"), 2, "Edda has only one mate: not identified");
});
