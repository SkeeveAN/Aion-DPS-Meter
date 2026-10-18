import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-estimated-")), "test.sqlite");
process.env.CLIENT_UPDATE_ENFORCE = "off";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { bossNpcIds, bosses, encounterParticipants, encounterSkillUsage, encounters, instances, players } = await import("./db/schema.js");
const { processUpload } = await import("./matching/merge.js");
const { topByClass, topGroups, statsForBossIds } = await import("./routes/bosses.js");
const { topPlayersOverall } = await import("./routes/players.js");
const { eq } = await import("drizzle-orm");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

function makeBoss(category: string, name: string, npcId: number, hp?: number) {
  const instance = db.insert(instances).values({ name: `${name} place`, nameEn: `${name} place`, game: "aion2", slug: `${name}-place`, category: category as "worldboss" }).returning().get();
  const boss = db.insert(bosses).values({ instanceId: instance.id, name, nameEn: name, slug: name.toLowerCase() }).returning().get();
  db.insert(bossNpcIds).values({ bossId: boss.id, npcId }).run();
  return { boss, hp };
}

const world = makeBoss("worldboss", "Worldy", 9100001);
const dungeon = makeBoss("dungeon", "Dungy", 9100002);

function row(name: string, className: string, damage: number, opts: { estimated?: boolean; isSelf?: boolean; skill?: string; critHits?: number } = {}) {
  return {
    name, className, faction: "Elyos", serverId: 1304, isSelf: opts.isSelf ?? false,
    totalDamage: damage, dps: damage / 100, idps: damage / 100, estimated: opts.estimated ?? false,
    totalHealing: 0, hps: 0, damageTaken: 0, damageAbsorbed: 0, shieldsGiven: [], buffs: [],
    skills: [{ skill: opts.skill ?? "Cleave", hits: 100, critHits: opts.critHits ?? 0, total: damage, min: 1, max: 100 }], healSkills: [],
  };
}

function upload(npcId: number, day: number, participants: ReturnType<typeof row>[], bossMaxHp = 1_000_000_000) {
  const start = Date.UTC(2026, 0, day, 20, 0, 0);
  return {
    clientVersion: "1.0.0", game: "aion2" as const, mode: "", bossNpcName: "x", bossNpcId: npcId, bossMaxHp,
    startedAt: new Date(start).toISOString(), endedAt: new Date(start + 150_000).toISOString(), serverFingerprint: "aion2:europe-kaisinel", participants,
  };
}

const post = (payload: unknown) => app.inject({ method: "POST", url: "/api/uploads", payload: payload as object });
const rowOf = (encounterId: number, name: string) =>
  db.select().from(encounterParticipants).innerJoin(players, eq(players.id, encounterParticipants.playerId)).where(eq(encounterParticipants.encounterId, encounterId)).all().find((r) => r.players.name === name)!.encounter_participants;

test("a world boss upload with estimated rows is accepted when the damage covers 75 % of the hit points, below that it is turned away", async () => {
  const low = await post(upload(9100001, 1, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 200_000, { estimated: true }), row("Cara", "Cleric", 100_000, { estimated: true })]));
  assert.equal(low.statusCode, 400);
  assert.equal(low.json().error, "boss_not_killed");
  const payload = upload(9100001, 1, [row("Anna", "Gladiator", 300_000_000, { isSelf: true }), row("Bob", "Assassin", 300_000_000, { estimated: true }), row("Cara", "Cleric", 160_000_000, { estimated: true })]);
  const response = await post(payload);
  assert.equal(response.statusCode, 200, response.body);
  const id = response.json().encounterId as number;
  assert.equal(rowOf(id, "Anna").estimated, false);
  assert.equal(rowOf(id, "Bob").estimated, true);
});

test("a world boss upload without estimated rows still needs the damage to cover the hit points; so does one without bossMaxHp", async () => {
  const exact = await post(upload(9100001, 2, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 200_000)]));
  assert.equal(exact.statusCode, 400);
  assert.equal(exact.json().error, "boss_not_killed");
  const withoutHp = upload(9100001, 2, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 200_000, { estimated: true })]);
  delete (withoutHp as { bossMaxHp?: number }).bossMaxHp;
  const noHp = await post(withoutHp);
  assert.equal(noHp.statusCode, 400);
});

test("the same upload for a boss that is not a world boss is turned away", async () => {
  const response = await post(upload(9100002, 3, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 200_000, { estimated: true })]));
  assert.equal(response.statusCode, 400);
  assert.equal(response.json().error, "boss_not_killed");
});

test("merge: an exact row stays when an estimate comes in", () => {
  const first = processUpload(upload(9100001, 10, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 200_000), row("Cara", "Cleric", 100_000)]));
  const second = processUpload(upload(9100001, 10, [row("Dora", "Ranger", 250_000, { isSelf: true }), row("Bob", "Assassin", 777_000, { estimated: true }), row("Cara", "Cleric", 100_000)]));
  assert.equal(second.encounterId, first.encounterId);
  const bob = rowOf(first.encounterId, "Bob");
  assert.equal(bob.estimated, false);
  assert.equal(bob.totalDamage, 200_000);
  assert.equal(db.select().from(encounterSkillUsage).where(eq(encounterSkillUsage.participantId, bob.id)).all()[0]!.totalDamage, 200_000);
});

test("merge: exact numbers replace an estimate, damage and skill rows included, and clear the flag", () => {
  const first = processUpload(upload(9100001, 11, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 90_000, { estimated: true, skill: "Guess" }), row("Cara", "Cleric", 100_000)]));
  assert.equal(rowOf(first.encounterId, "Bob").estimated, true);
  const second = processUpload(upload(9100001, 11, [row("Dora", "Ranger", 250_000, { isSelf: true }), row("Bob", "Assassin", 200_000, { skill: "Real", critHits: 7 }), row("Cara", "Cleric", 100_000)]));
  assert.equal(second.encounterId, first.encounterId);
  const bob = rowOf(first.encounterId, "Bob");
  assert.equal(bob.estimated, false);
  assert.equal(bob.totalDamage, 200_000);
  assert.deepEqual(db.select().from(encounterSkillUsage).where(eq(encounterSkillUsage.participantId, bob.id)).all().map((s) => s.skillName), ["Real"]);
});

test("merge: an estimated own row is replaced by the real one of the same player, even when the figures are far apart", () => {
  const first = processUpload(upload(9100001, 12, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 90_000, { estimated: true }), row("Cara", "Cleric", 100_000)]));
  const second = processUpload(upload(9100001, 12, [row("Bob", "Assassin", 400_000, { isSelf: true }), row("Anna", "Gladiator", 300_000), row("Cara", "Cleric", 100_000)]));
  assert.equal(second.encounterId, first.encounterId);
  assert.equal(db.select().from(encounterParticipants).where(eq(encounterParticipants.encounterId, first.encounterId)).all().length, 3);
  const bob = rowOf(first.encounterId, "Bob");
  assert.equal(bob.estimated, false);
  assert.equal(bob.totalDamage, 400_000);
});

test("merge: two estimates keep the flag", () => {
  const first = processUpload(upload(9100001, 13, [row("Anna", "Gladiator", 300_000, { isSelf: true }), row("Bob", "Assassin", 90_000, { estimated: true }), row("Cara", "Cleric", 100_000)]));
  const second = processUpload(upload(9100001, 13, [row("Dora", "Ranger", 250_000, { isSelf: true }), row("Bob", "Assassin", 95_000, { estimated: true }), row("Cara", "Cleric", 100_000)]));
  assert.equal(second.encounterId, first.encounterId);
  const bob = rowOf(first.encounterId, "Bob");
  assert.equal(bob.estimated, true);
});

test("rankings and averages ignore estimated rows", () => {
  // one fully exact kill on the dungeon boss, one world boss kill whose damage is estimated
  const exact = processUpload(upload(9100002, 20, [row("Eva", "Gladiator", 900_000, { isSelf: true }), row("Finn", "Assassin", 100_000)], 1_000_000));
  const guess = processUpload(upload(9100002, 21, [row("Eva", "Gladiator", 5_000_000, { isSelf: true }), row("Gus", "Ranger", 8_000_000, { estimated: true })], 1_000_000));
  assert.notEqual(exact.encounterId, guess.encounterId);

  const groups = topGroups(dungeon.boss.id, null, "aion2");
  assert.deepEqual(groups.map((g) => g.encounterId), [exact.encounterId]);
  const stats = statsForBossIds([dungeon.boss.id], null);
  assert.equal(stats.runCount, 2);
  assert.equal(stats.bestIdps, db.select().from(encounters).where(eq(encounters.id, exact.encounterId)).get()!.groupIDps);

  const byClass = topByClass(dungeon.boss.id, null, "aion2");
  assert.deepEqual(byClass["Assassin"]!.map((p) => p.playerName), ["Finn"]);
  assert.equal(byClass["Ranger"], undefined);

  const top = topPlayersOverall("aion2", null, 50);
  assert.ok(!top.some((t) => t!.playerName === "Gus"));
  // Eva's own row of the second kill is exact (5,000,000 / 100), only Gus's estimate is left out.
  assert.equal(top.find((t) => t!.playerName === "Eva")!.idps, 50_000);
});

test("a Verteron boss outside the rift hours: the crowd without server and faction is the uploader's; inside a rift hour nobody is guessed (no NC in the tests): the strangers are cut and the kill fails", async () => {
  makeBoss("worldboss", "Fieldy", 2101074);
  const bare = (name: string, className: string, damage: number) => {
    const r: Record<string, unknown> = { ...row(name, className, damage, { estimated: true }) };
    delete r.faction;
    delete r.serverId;
    return r as ReturnType<typeof row>;
  };
  const crowd = () => [row("Anchor", "Gladiator", 100_000_000, { isSelf: true }), bare("Strangerb", "Assassin", 400_000_000), bare("Strangerc", "Cleric", 300_000_000)];
  const at = (hour: number, minute: number) => ({ ...upload(2101074, 3, crowd()), serverName: "Europe - Kaisinel", startedAt: new Date(Date.UTC(2026, 0, 3, hour, minute, 0)).toISOString(), endedAt: new Date(Date.UTC(2026, 0, 3, hour, minute, 12)).toISOString() });
  const rift = await post(at(12, 30)); // 12:30 UTC: a rift hour (12:00 UTC)
  assert.equal(rift.statusCode, 400);
  assert.equal(rift.json().error, "boss_not_killed");
  assert.equal(db.select().from(players).where(eq(players.name, "Strangerb")).get(), undefined);
  const home = await post(at(10, 36)); // 10:36 UTC: no rift hour
  assert.equal(home.statusCode, 200, home.body);
  assert.equal(db.select().from(players).where(eq(players.name, "Strangerb")).get()?.faction, "Elyos");
  // the rule holds in every region
  const asia = { ...at(10, 36), serverName: "Asia - Kaisinel", serverFingerprint: "aion2:asia-kaisinel" };
  asia.participants = asia.participants.map((r) => ({ ...r, name: r.name.replace("Stranger", "Asiastranger"), ...(r.isSelf ? {} : {}) }));
  const abroad = await post({ ...asia, startedAt: new Date(Date.UTC(2026, 0, 4, 10, 36, 0)).toISOString(), endedAt: new Date(Date.UTC(2026, 0, 4, 10, 36, 12)).toISOString() });
  assert.equal(abroad.statusCode, 200, abroad.body);
  assert.equal(db.select().from(players).where(eq(players.name, "Asiastrangerb")).get()?.faction, "Elyos");
});
