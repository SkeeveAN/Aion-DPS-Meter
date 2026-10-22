import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdirSync, mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-bosshp-")), "test.sqlite");
process.env.UPLOAD_REVIEW = "on";
process.env.ADMIN_TOKEN = "secret-token-for-the-test";
process.env.CLIENT_UPDATE_ENFORCE = "off";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { encounterRoutes } = await import("./routes/encounters.js");
const { compareRoutes } = await import("./routes/compare.js");
const { uploads, encounters, bossHpReference } = await import("./db/schema.js");
const { processUpload } = await import("./matching/merge.js");
const { classifyFight, learnSpawnHp, referenceOf, seedBossHpReference } = await import("./bossHp.js");
const { statsForBossIds, topGroups, modesFor } = await import("./routes/bosses.js");
const { referenceFor } = await import("./uploadReview.js");
const { eq } = await import("drizzle-orm");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.register(encounterRoutes);
await app.register(compareRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

const RAUR = 2300206; // Guardian Captain Raur, not party-scaled, 5,220,000 HP (seed)
const HARCON_EXPLORE = 2310104; // Enhanced Harcon explore, party-scaled, 61,500 HP for one player (seed)
const TALISRA = 2330709; // Talisra of the Void, grows after the spawn (seed 11,067,000)

function participant(name: string, className: string, damage: number, isSelf = false) {
  return {
    name, className, faction: "Elyos", serverId: 1304, isSelf, totalDamage: damage, dps: 1, idps: damage / 100, totalHealing: 0, damageTaken: 0, damageAbsorbed: 0, shieldsGiven: [], buffs: [], hps: 0,
    skills: [{ skill: "Cleave", hits: 100, critHits: 20, total: damage, min: 1, max: 100 }], healSkills: [],
  };
}

const CLASSES = ["Gladiator", "Assassin", "Sorcerer", "Cleric", "Ranger"];
interface Opts { name?: string; npc?: number; day: number; maxHp: number; spawn?: number; people?: number; damage?: number }
/** A kill of `people` players whose group damage is `damage` (default: all of the highest HP reading), 150 s long. */
function fight(o: Opts) {
  const people = o.people ?? 2;
  const total = o.damage ?? o.maxHp;
  const start = Date.UTC(2026, 1, o.day, 20, 0, 0);
  return {
    clientVersion: "1.0.0", mode: "", game: "aion2" as const, bossNpcName: o.name ?? "Guardian Captain Raur", bossNpcId: o.npc ?? RAUR, bossMaxHp: o.maxHp,
    ...(o.spawn !== undefined ? { bossSpawnMaxHp: o.spawn } : {}),
    startedAt: new Date(start).toISOString(), endedAt: new Date(start + 150_000).toISOString(), serverFingerprint: "aion2:europe-kaisinel",
    participants: Array.from({ length: people }, (_, i) => participant(`Hero${i}`, CLASSES[i % 5]!, Math.floor(total / people), i === 0)),
  };
}
const post = (payload: unknown) => app.inject({ method: "POST", url: "/api/uploads", payload: payload as object });
const encounterOf = (id: number) => db.select().from(encounters).where(eq(encounters.id, id)).get()!;
const lastEncounter = () => db.select().from(encounters).all().at(-1)!;

test("the seed fills the reference once, never overwrites a row and is repeatable", () => {
  const first = seedBossHpReference();
  assert.ok(first > 100);
  assert.equal(referenceOf(RAUR)?.baseHp, 5_220_000);
  assert.equal(referenceOf(HARCON_EXPLORE)?.partyScaled, true);
  db.update(bossHpReference).set({ baseHp: 1234, source: "learned" }).where(eq(bossHpReference.npcId, 2300104)).run();
  assert.equal(seedBossHpReference(), 0);
  assert.equal(referenceOf(2300104)?.baseHp, 1234);
  // an own seed file adds only the missing id
  const dir = mkdtempSync(path.join(tmpdir(), "seed-"));
  mkdirSync(dir, { recursive: true });
  const file = path.join(dir, "seed.json");
  writeFileSync(file, JSON.stringify({ npcs: { "2300104": { baseHp: 9, partyScaled: false, source: "spawn" }, "9999991": { baseHp: 777, partyScaled: true, source: "spawn", uploads: 2, spawnFrames: 1 } } }));
  assert.equal(seedBossHpReference(file), 1);
  assert.equal(referenceOf(2300104)?.baseHp, 1234);
  assert.deepEqual(referenceOf(9999991), { baseHp: 777, partyScaled: true });
});

test("the bosses are put on file by one whole kill each (an unknown boss waits for review)", () => {
  processUpload(fight({ day: 0, maxHp: 5_220_000, spawn: 5_220_000 }) as never);
  processUpload(fight({ name: "Enhanced Harcon", npc: HARCON_EXPLORE, day: 0, maxHp: 61_500, spawn: 61_500, people: 1 }) as never);
  processUpload(fight({ name: "Talisra of the Void", npc: TALISRA, day: 0, maxHp: 11_067_000, spawn: 11_067_000 }) as never);
  assert.equal(db.select().from(encounters).all().length, 3);
});

test("classification: the examples of the rule", () => {
  const raur = { baseHp: 5_220_000, partyScaled: false };
  const harcon = { baseHp: 61_500, partyScaled: true };
  // not scaled: 2.1 M of 5.22 M seen is a partial fight at 40 %
  const partial = classifyFight({ bossMaxHp: 2_100_000 }, 3, raur, false);
  assert.equal(partial.isPartial, true);
  assert.ok(Math.abs(partial.hpSeenShare! - 2_100_000 / 5_220_000) < 1e-9);
  assert.equal(partial.bossRealMaxHp, 5_220_000);
  assert.equal(classifyFight({ bossMaxHp: 5_220_000 }, 3, raur, false).isPartial, false);
  // 99.9 % of the health is a whole fight whatever the damage sum was
  assert.equal(classifyFight({ bossMaxHp: 5_214_000 }, 3, raur, false).isPartial, false);
  // scaled: five people on 307,500 are whole, on 123,000 partial; two people on 123,000 are whole
  assert.equal(classifyFight({ bossMaxHp: 307_500 }, 5, harcon, false).isPartial, false);
  const late = classifyFight({ bossMaxHp: 123_000 }, 5, harcon, false);
  assert.equal(late.isPartial, true);
  assert.equal(late.bossRealMaxHp, 307_500);
  assert.equal(classifyFight({ bossMaxHp: 123_000 }, 2, harcon, false).isPartial, false);
  // the spawn value of the upload wins over the reference, and a spawn value alone is enough
  assert.equal(classifyFight({ bossMaxHp: 1_000_000, bossSpawnMaxHp: 5_220_000 }, 3, null, false).isPartial, true);
  assert.equal(classifyFight({ bossMaxHp: 1_000_000, bossSpawnMaxHp: 1_000_000 }, 3, null, false).hpSeenShare, 1);
  // a boss that grows after the spawn: readings above the spawn value make it the real maximum
  const talisra = classifyFight({ bossMaxHp: 11_067_000, bossSpawnMaxHp: 10_540_000 }, 4, { baseHp: 10_540_000, partyScaled: false }, false);
  assert.equal(talisra.isPartial, false);
  assert.equal(talisra.bossRealMaxHp, 11_067_000);
  assert.equal(talisra.hpSeenShare, 1);
  // unknown boss: old behaviour
  const unknown = classifyFight({ bossMaxHp: 1_000 }, 3, null, false);
  assert.deepEqual([unknown.isPartial, unknown.hpSeenShare, unknown.bossRealMaxHp], [false, null, null]);
  // world boss: never partial
  assert.equal(classifyFight({ bossMaxHp: 100 }, 3, raur, true).isPartial, false);
  // a spawn value that does not fit the reference is not trusted: the reference speaks
  assert.equal(classifyFight({ bossMaxHp: 5_220_000, bossSpawnMaxHp: 100_000 }, 3, raur, false).isPartial, false);
});

test("classification: the group's damage covers a quick fight whose first reading came late", () => {
  const raur = { baseHp: 5_220_000, partyScaled: false };
  // the top reading is 94 % because the first hits came before the first reading, but the hits counted cover the whole boss: whole fight
  const quick = classifyFight({ bossMaxHp: 4_907_000 }, 3, raur, false, 5_300_000);
  assert.equal(quick.isPartial, false);
  assert.ok(Math.abs(quick.hpSeenShare! - 4_907_000 / 5_220_000) < 1e-9);
  // joined really late: reading and damage both low, partial
  assert.equal(classifyFight({ bossMaxHp: 4_800_000 }, 3, raur, false, 4_790_000).isPartial, true);
  assert.equal(classifyFight({ bossMaxHp: 2_100_000 }, 3, raur, false, 2_090_000).isPartial, true);
  // no damage given: the reading alone judges
  assert.equal(classifyFight({ bossMaxHp: 4_907_000 }, 3, raur, false).isPartial, true);
  // a boss whose real maximum is above the reading: damage of 95 % of the REAL maximum is needed
  assert.equal(classifyFight({ bossMaxHp: 2_100_000 }, 3, raur, false, 2_200_000).isPartial, true);
});

test("the reference learns from spawn values, ignores suspicious ones", () => {
  const id = 8_000_001;
  learnSpawnHp(id, undefined);
  assert.equal(referenceOf(id), null);
  learnSpawnHp(id, 1_000_000); // one report is enough
  assert.deepEqual(referenceOf(id), { baseHp: 1_000_000, partyScaled: false });
  learnSpawnHp(id, 1_000_000);
  assert.equal(db.select().from(bossHpReference).where(eq(bossHpReference.npcId, id)).get()!.samples, 2);
  learnSpawnHp(id, 400_000); // far below: ignored
  learnSpawnHp(id, 3_000_000); // far above: ignored
  learnSpawnHp(id, 1_300_000); // more than +20 %: ignored
  assert.equal(referenceOf(id)!.baseHp, 1_000_000);
  learnSpawnHp(id, 1_100_000); // within +20 %: a boss that is not scaled keeps the highest consistent value
  assert.equal(referenceOf(id)!.baseHp, 1_100_000);
  // a scaled boss keeps its single-player value; a multiple of it is consistent but changes nothing
  learnSpawnHp(HARCON_EXPLORE, 307_500);
  learnSpawnHp(HARCON_EXPLORE, 61_500);
  assert.equal(referenceOf(HARCON_EXPLORE)!.baseHp, 61_500);
});

test("an upload with the boss's late-join reading is accepted as a partial fight, not reviewed, and stays out of rankings", async () => {
  // the boss on file first: a whole kill
  const whole = await post(fight({ day: 1, maxHp: 5_220_000, spawn: 5_220_000, people: 3 }));
  assert.equal(whole.statusCode, 200, whole.body);
  const wholeRow = lastEncounter();
  assert.equal(wholeRow.isPartial, false);
  assert.equal(wholeRow.hpSeenShare, 1);
  assert.equal(wholeRow.bossRealMaxHp, 5_220_000);
  assert.equal(wholeRow.bossSpawnMaxHp, 5_220_000);

  // a late joiner: 2.1 M of 5.22 M, the damage adds up to what he saw (damage >= 90 % rule holds)
  const late = await post(fight({ day: 2, maxHp: 2_100_000, spawn: 5_220_000 }));
  assert.equal(late.statusCode, 200, late.body);
  assert.notEqual(late.json().status, "pending_review");
  const partialRow = lastEncounter();
  assert.equal(partialRow.isPartial, true);
  assert.ok(Math.abs(partialRow.hpSeenShare! - 0.4023) < 0.001);
  assert.equal(partialRow.bossMaxHp, 2_100_000);

  // an old client without the spawn field: the reference of the NPC id judges
  const old = await post(fight({ day: 3, maxHp: 1_000_000 }));
  assert.equal(old.statusCode, 200, old.body);
  const oldRow = lastEncounter();
  assert.equal(oldRow.isPartial, true);
  assert.equal(oldRow.bossSpawnMaxHp, null);
  assert.equal(oldRow.bossRealMaxHp, 5_220_000);

  // the old rule is unchanged: damage far below the reading is no kill
  const bad = await post(fight({ day: 4, maxHp: 5_220_000, damage: 2_000_000 }));
  assert.equal(bad.statusCode, 400);
  assert.equal(bad.json().error, "boss_not_killed");

  // 99.9 % of the health, but only 92 % of it dealt as damage: still a whole kill
  const close = await post(fight({ day: 5, maxHp: 5_214_000, damage: Math.floor(5_214_000 * 0.92) }));
  assert.equal(close.statusCode, 200, close.body);
  assert.equal(lastEncounter().isPartial, false);

  // rankings, counts and review references see the whole fights only
  const bossId = wholeRow.bossId;
  const stats = statsForBossIds([bossId], null, null);
  assert.equal(stats.runCount, 3); // day 0, 1 and 5
  const groups = topGroups(bossId, null, "aion2");
  assert.equal(groups.length, 3);
  assert.ok(groups.every((g) => g.isPartial === false));
  assert.equal(modesFor(bossId, "expedition").length, 0);
  assert.equal(db.select().from(encounters).where(eq(encounters.bossId, bossId)).all().length, 5);
  const reference = referenceFor(fight({ day: 9, maxHp: 5_220_000 }) as never);
  assert.equal(reference.runs, 3);
  assert.ok(!reference.knownHps.includes(2_100_000) && !reference.knownHps.includes(1_000_000));

  // lists of single fights still show it, flagged
  const picker = await app.inject({ method: "GET", url: `/api/bosses/${bossId}/runs` });
  const runs = picker.json().runs as { encounterId: number; isPartial: boolean }[];
  assert.equal(runs.length, 5);
  assert.equal(runs.filter((r) => r.isPartial).length, 2);

  // the picker names everyone in each fight and filters by group, player, sort order and the fight it compares from
  type PickerRun = { encounterId: number; groupIDps: number; durationSeconds: number; members: { playerId: number; playerName: string }[] };
  const pick = async (query: string) => (await app.inject({ method: "GET", url: `/api/bosses/${bossId}/runs?${query}` })).json() as { total: number; runs: PickerRun[] };
  const all = (await pick("")).runs;
  assert.ok(all.every((r) => r.members.length >= 2));
  const trio = all.find((r) => r.members.length === 3)!;
  const sameGroup = await pick(`group=${trio.encounterId}`);
  assert.ok(sameGroup.runs.length >= 1 && sameGroup.runs.every((r) => r.members.length === 3));
  assert.equal(sameGroup.total, sameGroup.runs.length);
  const withHero2 = await pick("player=" + trio.members.find((m) => m.playerName === "Hero2")!.playerId);
  assert.ok(withHero2.runs.length >= 1 && withHero2.runs.every((r) => r.members.some((m) => m.playerName === "Hero2")));
  const byIdps = (await pick("sort=idps")).runs.map((r) => r.groupIDps);
  assert.deepEqual(byIdps, [...byIdps].sort((x, y) => y - x));
  const byTime = (await pick("sort=fastest")).runs.map((r) => r.durationSeconds);
  assert.deepEqual(byTime, [...byTime].sort((x, y) => x - y));
  const without = await pick(`exclude=${trio.encounterId}`);
  assert.equal(without.total, all.length - 1);
  assert.ok(without.runs.every((r) => r.encounterId !== trio.encounterId));
  const detail = (await app.inject({ method: "GET", url: `/api/encounters/${partialRow.id}` })).json().encounter;
  assert.equal(detail.isPartial, true);
  assert.ok(Math.abs(detail.hpSeenShare - 0.4023) < 0.001);
  assert.equal(detail.bossRealMaxHp, 5_220_000);
  assert.equal(detail.bossSpawnMaxHp, 5_220_000);
  const recent = (await app.inject({ method: "GET", url: "/api/activity/recent?limit=20" })).json() as { encounterId: number; isPartial: boolean }[];
  assert.equal(recent.find((r) => r.encounterId === partialRow.id)?.isPartial, true);
});

test("the spawn value of an accepted upload teaches the reference of an unknown NPC id", () => {
  assert.equal(referenceOf(2300999), null);
  // (through processUpload: the route's review would hold an HP value nobody has seen for the boss; only an accepted upload teaches the reference)
  processUpload(fight({ name: "Guardian Captain Raur", npc: 2300999, day: 6, maxHp: 500_000, spawn: 500_000 }) as never);
  assert.equal(referenceOf(2300999)?.baseHp, 500_000);
});

test("merging: an encounter is partial only while all its uploads are", () => {
  const at = { startedAt: "2026-03-01T20:00:00.000Z", endedAt: "2026-03-01T20:02:30.000Z" };
  const mk = (maxHp: number, spawn: number | undefined, people = 2) => ({ ...fight({ day: 10, maxHp, spawn, people }), ...at }) as never;

  // partial first, a whole upload of the same fight afterwards: becomes whole and takes the whole upload's values
  const created = processUpload(mk(1_000_000, 5_220_000));
  assert.equal(encounterOf(created.encounterId).isPartial, true);
  const merged = processUpload(mk(5_220_000, 5_220_000));
  assert.equal(merged.status, "merged");
  assert.equal(merged.encounterId, created.encounterId);
  const row = encounterOf(created.encounterId);
  assert.equal(row.isPartial, false);
  assert.equal(row.bossMaxHp, 5_220_000);
  assert.equal(row.hpSeenShare, 1);
  assert.equal(row.bossRealMaxHp, 5_220_000);
  assert.equal(row.mergedUploadCount, 2);

  // whole first, a partial afterwards: stays whole
  const at2 = { startedAt: "2026-03-02T20:00:00.000Z", endedAt: "2026-03-02T20:02:30.000Z" };
  const a = processUpload({ ...fight({ day: 11, maxHp: 5_220_000, spawn: 5_220_000 }), ...at2 } as never);
  processUpload({ ...fight({ day: 11, maxHp: 1_000_000, spawn: 5_220_000 }), ...at2 } as never);
  assert.equal(encounterOf(a.encounterId).isPartial, false);
  assert.equal(encounterOf(a.encounterId).bossMaxHp, 5_220_000);

  // two partials: stays partial and keeps the higher reading
  const at3 = { startedAt: "2026-03-03T20:00:00.000Z", endedAt: "2026-03-03T20:02:30.000Z" };
  const b = processUpload({ ...fight({ day: 12, maxHp: 1_000_000, spawn: 5_220_000 }), ...at3 } as never);
  processUpload({ ...fight({ day: 12, maxHp: 2_000_000, spawn: 5_220_000 }), ...at3 } as never);
  const two = encounterOf(b.encounterId);
  assert.equal(two.isPartial, true);
  assert.equal(two.bossMaxHp, 2_000_000);
  assert.ok(Math.abs(two.hpSeenShare! - 2_000_000 / 5_220_000) < 1e-9);
});

test("party-scaled boss: the party size decides", async () => {
  const all = (people: number, maxHp: number, day: number) => post(fight({ name: "Enhanced Harcon", npc: HARCON_EXPLORE, day, maxHp, spawn: 61_500, people }));
  assert.equal((await all(5, 307_500, 20)).statusCode, 200);
  assert.equal(lastEncounter().isPartial, false);
  assert.equal((await all(5, 123_000, 21)).statusCode, 200);
  assert.equal(lastEncounter().isPartial, true);
  assert.equal(lastEncounter().bossRealMaxHp, 307_500);
  assert.equal((await all(2, 123_000, 22)).statusCode, 200);
  assert.equal(lastEncounter().isPartial, false);
});

test("Talisra: the readings above the spawn value make the real maximum, the fight is whole", async () => {
  const response = await post(fight({ name: "Talisra of the Void", npc: TALISRA, day: 23, maxHp: 11_067_000, spawn: 10_540_000, people: 4 }));
  assert.equal(response.statusCode, 200, response.body);
  const row = lastEncounter();
  assert.equal(row.isPartial, false);
  assert.equal(row.bossRealMaxHp, 11_067_000);
  assert.equal(row.bossSpawnMaxHp, 10_540_000);
});

test("the uploads of partial fights are kept like any other", () => {
  const merged = db.select().from(uploads).all().filter((u) => u.status === "merged").length;
  assert.ok(merged >= 5, `${merged} merged uploads`);
});

test("the player comparison counts whole fights only", async () => {
  // Hero0 of the Raur fights: two whole ones, two partial ones - the ranked lists see two runs
  const { players } = await import("./db/schema.js");
  const hero = db.select().from(players).all().find((p) => p.name === "Hero0")!;
  const bosses = (await app.inject({ method: "GET", url: `/api/players/${hero.id}/bosses` })).json() as { bossName: string; runs: number }[];
  const raur = bosses.find((b) => b.bossName === "Guardian Captain Raur")!;
  const rows = sqlite.prepare("select e.is_partial p from encounter_participants ep join encounters e on e.id = ep.encounter_id where ep.player_id = ? and e.boss_id = (select id from bosses where name = 'Guardian Captain Raur')").all(hero.id) as { p: number }[];
  assert.ok(rows.some((r) => r.p === 1)); // there are partial fights ...
  assert.equal(raur.runs, rows.filter((r) => r.p === 0).length); // ... and none of them is counted
});
