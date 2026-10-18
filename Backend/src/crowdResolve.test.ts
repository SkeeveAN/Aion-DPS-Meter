import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-crowd-")), "test.sqlite");
process.env.CLIENT_UPDATE_ENFORCE = "off";
process.env.CROWD_LOOKUP = "on";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { bossNpcIds, bosses, encounters, instances, players, uploads } = await import("./db/schema.js");
const { syncMatchmakingToDb } = await import("./serverMatchmakingDb.js");
const { settleCrowdPending, candidateServers, riftOpen } = await import("./crowdResolve.js");
const { eq } = await import("drizzle-orm");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

syncMatchmakingToDb([{ region: "Europe", noticeId: "n1", pairs: [{ region: "Europe", elyos: "Kaisinel", asmodian: "Lumiel", phase: null, updated: null }] }], 1000);
const instance = db.insert(instances).values({ name: "Verteron", nameEn: "Verteron", game: "aion2", slug: "verteron-x", category: "worldboss" }).returning().get();
const boss = db.insert(bosses).values({ instanceId: instance.id, name: "Gartuy", nameEn: "Gartuy", slug: "gartuy" }).returning().get();
db.insert(bossNpcIds).values({ bossId: boss.id, npcId: 9300001 }).run();

const row = (name: string, className: string, damage: number, extra: object = {}) => ({
  name, className, faction: "", isSelf: false, totalDamage: damage, dps: damage / 1000, idps: damage / 1000, estimated: true,
  totalHealing: 0, hps: 0, damageTaken: 0, damageAbsorbed: 0, shieldsGiven: [], buffs: [],
  skills: [{ skill: "Cleave", hits: 10, critHits: 0, total: damage, min: 1, max: 100 }], healSkills: [], ...extra,
});
const self = row("Anna", "Gladiator", 300_000_000, { faction: "Elyos", serverId: 1304, isSelf: true, estimated: false });
const start = Date.UTC(2026, 0, 5, 20, 0, 0);
const payload = {
  clientVersion: "1.0.0", game: "aion2" as const, mode: "", bossNpcName: "Gartuy", bossNpcId: 9300001, bossMaxHp: 1_000_000_000,
  startedAt: new Date(start).toISOString(), endedAt: new Date(start + 12_000).toISOString(), serverFingerprint: "aion2:europe-kaisinel", serverName: "Europe - Kaisinel",
  participants: [self, row("Bob", "Assassin", 400_000_000), row("Cara", "Cleric", 100_000_000), row("Dan", "Ranger", 300_000_000), row("Eve", "Ranger", 200_000_000), row("Fay", "Templar", 100_000_000)],
};

// class = floor((pcId-1)/4): Templar 2 = pcId 9, Ranger 3 = 13, Assassin 4 = 17, Sorcerer 6 = 25, Cleric 7 = 29
const hit = (name: string, race: number, serverId: number, pcId: number) => ({ name, race, serverId, pcId, level: 45, characterId: `${name}${serverId}${pcId}` });
const world: Record<string, object[]> = {
  Bob: [hit("Bob", 1, 1304, 17), hit("Bob", 1, 1305, 17)], // once on the pairing's servers (the other one is none of them)
  Cara: [hit("Cara", 2, 2304, 29), hit("Cara", 1, 1304, 25)], // on both servers: the class tells (Cleric on Lumiel, Sorcerer on Kaisinel)
  Dan: [hit("Dan", 1, 1304, 13), hit("Dan", 2, 2304, 13)], // on both servers, same class: not told apart
  Eve: [hit("Eve", 1, 1311, 13)], // only elsewhere
};
const nc = (async (input: unknown) => {
  const keyword = new URL(String(input)).searchParams.get("keyword") ?? "";
  const list = world[keyword] ?? [];
  return new Response(JSON.stringify({ list, pagination: { page: 1, size: 40, total: list.length, endPage: 1 } }), { status: 200, headers: { "content-type": "application/json" } });
}) as unknown as typeof fetch;

test("the candidates are the uploader's server and the one it is paired with", () => {
  assert.deepEqual(candidateServers(payload as never).sort(), [1304, 2304]);
});

test("a world boss crowd waits, is looked up at NC on both servers, and only players found exactly once get server and faction", async () => {
  const res = await app.inject({ method: "POST", url: "/api/uploads", payload });
  assert.equal(res.statusCode, 200, res.body);
  assert.deepEqual(res.json().reasons, ["crowd_pending"]);
  assert.equal(db.select().from(encounters).all().length, 0, "nothing is filed before the lookup");

  while (await settleCrowdPending(app.log, 2, nc)) {
    // rounds of two names
  }
  const encs = db.select().from(encounters).all();
  assert.equal(encs.length, 1);
  const names = Object.fromEntries(db.select().from(players).all().map((p) => [p.name, p]));
  assert.equal(names.Bob.faction, "Elyos");
  assert.equal(names.Cara.faction, "Asmodian", "the class (Cleric) told the two apart: Asmodian Cleric on Lumiel");
  assert.equal(names.Dan, undefined, "two characters of his class: not placed, so no server and no faction - and not stored");
  assert.equal(names.Eve, undefined, "found only on another server: not placed");
  assert.equal(names.Fay, undefined, "not found at all");
  const waiting = db.select().from(uploads).where(eq(uploads.status, "pending")).all();
  assert.equal(waiting.length, 0);
});

test("a rift hour is the hour after each of 00:00, 03:00 ... 21:00 UTC (with two minutes of slack)", () => {
  const utc = (h: number, m: number) => Date.UTC(2026, 9, 5, h, m, 0);
  for (const h of [0, 3, 6, 9, 12, 15, 18, 21]) {
    assert.equal(riftOpen(utc(h, 30), utc(h, 30) + 12_000), true, `${h}:30 UTC`);
  }
  for (const [h, m] of [[10, 36], [13, 30], [16, 30], [19, 30], [22, 30], [1, 30], [4, 30]]) {
    assert.equal(riftOpen(utc(h, m), utc(h, m) + 12_000), false, `${h}:${m} UTC`);
  }
  assert.equal(riftOpen(utc(11, 59), utc(11, 59) + 12_000), true, "slack before the hour");
  assert.equal(riftOpen(utc(13, 5), utc(13, 5) + 12_000), false, "the hour is over");
});
