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

const { processUpload, processProfilesUpload } = await import("./merge.js");
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
        estimated: false,
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
        estimated: false,
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
          estimated: false,
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
          estimated: false,
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
          estimated: false,
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
          estimated: false,
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
          estimated: false,
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
          estimated: false,
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

test("a player keeps the faction a fight or a profile upload reported, and a later upload without one does not erase it", () => {
  const profileUpload = (faction: string) =>
    processProfilesUpload({
      clientVersion: "test",
      game: "aion2" as const,
      serverFingerprint: "aion2:europe-kaisinel",
      serverName: "Europe - Kaisinel",
      participants: [{ name: "Gruppenfreund", className: "Cleric", faction, isSelf: false, profile: { source: "seen" as const, classId: 7, gear: [] } }],
    } as unknown as Parameters<typeof processProfilesUpload>[0]);
  const factionOf = (name: string) => (sqlite.prepare("select faction f from players where name_normalized = ?").get(name.toLowerCase()) as { f: string }).f;

  profileUpload("");
  assert.equal(factionOf("Gruppenfreund"), "", "an old client sends none");
  profileUpload("Asmodian");
  assert.equal(factionOf("Gruppenfreund"), "Asmodian", "a profile upload from a client that knows it stores it");
  profileUpload("");
  assert.equal(factionOf("Gruppenfreund"), "Asmodian", "an upload without a faction leaves it");

  upload([member("Kaempferin", "Elyos")], false, "Europe - Kaisinel", "aion2:europe-kaisinel");
  assert.equal(factionOf("Kaempferin"), "Elyos", "a fight stores it too");
});

test("a participant without a faction gets that of the server he announced himself with", () => {
  upload([member("Ohnefraktion", "", { serverId: 2308 })], true, "Europe - Kaisinel", "aion2:europe-kaisinel");
  const f = (sqlite.prepare("select faction f from players where name_normalized = 'ohnefraktion'").get() as { f: string }).f;
  assert.equal(f, "Asmodian", "2308 is an Asmodian server");
});

test("the difficulty of an Ascension Rite boss comes from its level, else from the ids known so far, never from the rank of the id", () => {
  const instance = db.insert(instances).values({ name: "Sanctum Test", nameEn: "Sanctum Test", game: "aion2", slug: "sanctum-test", category: "ascension" }).returning().get();
  const boss = db.insert(bosses).values({ name: "Featherstorm Test", instanceId: instance.id }).returning().get();
  for (const npcId of [2701774, 2701829, 2701841, 2701853, 2709999]) {
    db.insert(bossNpcIds).values({ bossId: boss.id, npcId }).run();
  }
  const modeOf = (npcId: number, bossLevel?: number) => {
    const out = processUpload(basePayload({ bossNpcName: "Featherstorm Test", bossNpcId: npcId, bossLevel, startedAt: `2026-03-0${bossLevel ? 1 : 2}T20:00:00.000Z`, endedAt: `2026-03-0${bossLevel ? 1 : 2}T20:03:12.000Z` }));
    return (sqlite.prepare("select mode from encounters where id = ?").get(out.encounterId) as { mode: string }).mode;
  };
  assert.equal(modeOf(2701774, 60), "extreme", "the level of a new client wins");
  assert.equal(modeOf(2701774, 45), "easy", "even against the id (the level is what the game says)");
  assert.equal(modeOf(2701774), "extreme", "an old client's id 2701774 is known as Extreme, though it is the lowest id of the boss");
  assert.equal(modeOf(2701853), "easy", "and the highest id is Easy");
  assert.equal(modeOf(2709999), "", "an id that is not known has no step");
});

test("a world boss kill uploaded first with a few names and later with the whole crowd is one encounter; an instance boss is not merged that way", () => {
  const crowdOf = (names: string[]) =>
    names.map((name, i) => ({ ...basePayload().participants[1], name, isSelf: i === 0, totalDamage: 900_000 - i * 1000 }));
  const names = (n: number) => Array.from({ length: n }, (_, i) => `Held${i}`);
  for (const [category, boss] of [["worldboss", "Crowd Boss"], ["expedition", "Group Boss"]] as const) {
    const instance = db.insert(instances).values({ name: `${boss} place`, nameEn: `${boss} place`, game: "aion2", slug: `${boss}-place`, category }).returning().get();
    db.insert(bosses).values({ name: boss, instanceId: instance.id }).run();
  }
  const upload = (boss: string, count: number) =>
    processUpload(basePayload({ bossNpcName: boss, participants: crowdOf(names(count)), startedAt: "2026-04-01T20:00:00.000Z", endedAt: "2026-04-01T20:03:12.000Z" }));

  const small = upload("Crowd Boss", 9);
  const large = upload("Crowd Boss", 300);
  assert.equal(large.encounterId, small.encounterId, "the nine names lie in the crowd: the same kill");

  const smallGroup = upload("Group Boss", 9);
  const largeGroup = upload("Group Boss", 40);
  assert.notEqual(largeGroup.encounterId, smallGroup.encounterId, "an instance boss still needs a Jaccard of 0.5 (9 of 40 is not)");
});

test("the same kill seen to begin 22 seconds later by another uploader is one encounter, while a different end is not", () => {
  const instance = db.insert(instances).values({ name: "Late place", nameEn: "Late place", game: "aion2", slug: "late-place", category: "expedition" }).returning().get();
  db.insert(bosses).values({ name: "Late Boss", instanceId: instance.id }).run();
  const at = (start: string, end: string) => processUpload(basePayload({ bossNpcName: "Late Boss", startedAt: start, endedAt: end }));
  const first = at("2026-05-01T16:53:05.600Z", "2026-05-01T16:54:14.000Z");
  const late = at("2026-05-01T16:53:27.900Z", "2026-05-01T16:54:14.400Z");
  assert.equal(late.encounterId, first.encounterId, "start 22 s later, end the same: the same kill");
  const other = at("2026-05-01T16:53:27.900Z", "2026-05-01T16:55:14.400Z");
  assert.notEqual(other.encounterId, first.encounterId, "another end is another fight");
});

test("a world boss kill with exactly the same hit points at the same moment is one encounter even when the crowds hardly overlap", () => {
  const instance = db.insert(instances).values({ name: "Field place", nameEn: "Field place", game: "aion2", slug: "field-place", category: "worldboss" }).returning().get();
  db.insert(bosses).values({ name: "Field Boss", instanceId: instance.id }).run();
  const crowd = (prefix: string, count: number) =>
    Array.from({ length: count }, (_, i) => ({ ...basePayload().participants[1], name: `${prefix}${i}`, isSelf: i === 0, totalDamage: 900_000 - i * 1000 }));
  const at = (prefix: string, count: number, hp: number | undefined, end: string) =>
    processUpload(basePayload({ bossNpcName: "Field Boss", bossMaxHp: hp, participants: crowd(prefix, count), startedAt: "2026-06-01T22:35:45.000Z", endedAt: end }));
  const first = at("A", 21, 311_945_047, "2026-06-01T22:35:58.000Z");
  const second = at("B", 30, 311_945_047, "2026-06-01T22:36:00.500Z");
  assert.equal(second.encounterId, first.encounterId, "same hit points, same moment: the same kill");
  const noHp = at("C", 30, undefined, "2026-06-01T22:36:00.500Z");
  assert.notEqual(noHp.encounterId, first.encounterId, "without the number the rosters decide, and they do not overlap");
});

test("a character renamed between two uploads of the same fight stays one participant, whichever name an upload uses", () => {
  const at = (name: string, isSelf: boolean) =>
    processUpload(
      basePayload({
        bossNpcName: "Renamed Boss",
        startedAt: "2026-07-01T18:00:00.000Z",
        endedAt: "2026-07-01T18:01:30.000Z",
        participants: [
          ...["Anna", "Cora", "Dino", "Emil"].map((n) => ({ ...basePayload().participants[0], name: n, isSelf: false })),
          { ...basePayload().participants[1], name, isSelf },
        ],
      }),
    );
  const instance = db.insert(instances).values({ name: "Rename place", nameEn: "Rename place", game: "aion2", slug: "rename-place", category: "expedition" }).returning().get();
  db.insert(bosses).values({ name: "Renamed Boss", instanceId: instance.id }).run();

  const first = at("Oldname", true);
  // The NC check renames the character; the old name stays as an alias.
  db.update(players).set({ name: "Newname", nameNormalized: "newname", aliasNamesNormalized: ["oldname"] }).where(eq(players.name, "Oldname")).run();
  const second = at("Oldname", true);
  const third = at("Oldname", false);
  assert.equal(second.encounterId, first.encounterId);
  assert.equal(third.encounterId, first.encounterId);

  const rows = db.select().from(encounterParticipants).where(eq(encounterParticipants.encounterId, first.encounterId)).all();
  const ids = rows.map((r) => r.playerId);
  assert.equal(new Set(ids).size, ids.length, "no player twice in one fight");
  assert.equal(rows.length, 5);
});

test("the time axis of a fight is stored once per player: the upload with the most skill uses stays, and the boss health curve is kept", async () => {
  const { encounterTimelines } = await import("../db/schema.js");
  const { loadEncounterTimeline } = await import("../encounterTimeline.js");
  const instance = db.insert(instances).values({ name: "Timeline place", nameEn: "Timeline place", game: "aion2", slug: "timeline-place", category: "expedition" }).returning().get();
  db.insert(bosses).values({ name: "Timeline Boss", instanceId: instance.id }).run();
  const timeline = (times: number[]) => ({ skills: [{ id: 1, name: "Rending Blow" }], times, index: times.map(() => 0), deaths: [] });
  const at = (start: string, uses: number[], bossHp: number[]) =>
    processUpload(
      basePayload({
        bossNpcName: "Timeline Boss",
        startedAt: start,
        endedAt: "2026-08-01T18:01:30.000Z",
        bossHp,
        participants: basePayload().participants.map((p) => (p.name === "Anna" ? { ...p, timeline: timeline(uses) } : p)),
      }),
    );
  const first = at("2026-08-01T18:00:00.000Z", [10, 30], [0, 1000, 100, 900]);
  const second = at("2026-08-01T18:00:01.000Z", [10, 30, 50, 70], [0, 1000, 20, 980, 40, 950, 60, 900, 80, 850]);
  const third = at("2026-08-01T18:00:00.000Z", [10], []);
  assert.equal(second.encounterId, first.encounterId);
  assert.equal(third.encounterId, first.encounterId);
  const rows = db.select().from(encounterTimelines).all();
  const { timelineJsonOf } = await import("../uploadArchive.js");
  const anna = rows.filter((r) => JSON.parse(timelineJsonOf(r)!).times.length === 4);
  assert.equal(anna.length, 1, "the longest axis is kept, the shorter ones are not stored over it");
  const shaped = loadEncounterTimeline(first.encounterId)!;
  assert.deepEqual(shaped.players.map((p) => p.name), ["Anna"]);
  assert.equal(shaped.players[0]!.uses.length, 4);
  assert.equal(shaped.players[0]!.uses[0]![0], 2, "the second upload began one second later: its 1.0 s is 2.0 s on the encounter's clock");
  assert.equal(shaped.bossHp.length, 5);
});

test("the analysis of a time axis is stored with it: an upload that brings one replaces an axis without, one without never replaces one that has it", async () => {
  const { loadEncounterTimeline } = await import("../encounterTimeline.js");
  const instance = db.insert(instances).values({ name: "Analysis place", nameEn: "Analysis place", game: "aion2", slug: "analysis-place", category: "expedition" }).returning().get();
  db.insert(bosses).values({ name: "Analysis Boss", instanceId: instance.id }).run();
  const timeline = (times: number[], analysis?: { damage: number[]; addDamage: number; dead: number[]; taken: never[] }) => ({ skills: [{ id: 1, name: "Rending Blow" }], times, index: times.map(() => 0), deaths: [], ...(analysis ? { analysis } : {}) });
  const at = (start: string, tl: ReturnType<typeof timeline>) =>
    processUpload(
      basePayload({
        bossNpcName: "Analysis Boss",
        startedAt: start,
        endedAt: "2026-08-02T18:01:30.000Z",
        participants: basePayload().participants.map((p) => (p.name === "Anna" ? { ...p, timeline: tl } : p)),
      }),
    );
  const first = at("2026-08-02T18:00:00.000Z", timeline([10, 30, 50]));
  assert.equal(loadEncounterTimeline(first.encounterId)!.analysis, undefined, "an older client sends none");
  at("2026-08-02T18:00:00.000Z", timeline([10], { damage: [100, 200], addDamage: 5, dead: [], taken: [] }));
  const withAnalysis = loadEncounterTimeline(first.encounterId)!;
  assert.deepEqual(withAnalysis.analysis?.groupDamage.slice(0, 2), [100, 200], "the upload with the analysis replaced the one without");
  at("2026-08-02T18:00:00.000Z", timeline([10, 20, 30, 40]));
  assert.deepEqual(loadEncounterTimeline(first.encounterId)!.analysis?.groupDamage.slice(0, 2), [100, 200], "a longer axis without an analysis does not replace one that has it");
  at("2026-08-02T18:00:00.000Z", timeline([10, 20, 30, 40], { damage: [999], addDamage: 0, dead: [], taken: [] }));
  assert.deepEqual(loadEncounterTimeline(first.encounterId)!.analysis?.groupDamage.slice(0, 1), [999], "a longer axis keeps its own analysis whole, never mixed with the older one");
});

test("a plain time axis is packed by the archive job and reads back unchanged", async () => {
  const { encounterTimelines } = await import("../db/schema.js");
  const { eq } = await import("drizzle-orm");
  const { compressTimelines, timelineJsonOf } = await import("../uploadArchive.js");
  const { sqlite } = await import("../db/client.js");
  const row = db.select().from(encounterTimelines).all()[0]!;
  const before = timelineJsonOf(row)!;
  sqlite.prepare("update encounter_timelines set data_json = ?, data_gz = null where participant_id = ?").run(before, row.participantId);
  assert.equal(compressTimelines(), 1);
  const after = db.select().from(encounterTimelines).where(eq(encounterTimelines.participantId, row.participantId)).get()!;
  assert.equal(after.dataJson, "");
  assert.equal(timelineJsonOf(after), before, "gzip loses nothing");
});
