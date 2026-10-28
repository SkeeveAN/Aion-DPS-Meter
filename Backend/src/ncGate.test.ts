import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-ncgate-")), "test.sqlite");
process.env.UPLOAD_REVIEW = "off";
process.env.CLIENT_UPDATE_ENFORCE = "off";
process.env.NC_GATE = "on";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { uploads, encounters, players, playerNcMatch } = await import("./db/schema.js");
const { settleNcPending } = await import("./ncGate.js");
const { eq } = await import("drizzle-orm");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

function participant(name: string, className: string, damage: number, isSelf = false) {
  return {
    name, className, faction: "Elyos", serverId: 1304, isSelf, totalDamage: damage, dps: 1, idps: 1, totalHealing: 0, damageTaken: 0, damageAbsorbed: 0, shieldsGiven: [], buffs: [], hps: 0,
    skills: [{ skill: "Cleave", hits: 100, critHits: 20, total: damage, min: 1, max: 100 }], healSkills: [],
  };
}

function body(day: number, names: string[]) {
  const start = Date.UTC(2026, 0, day, 20, 0, 0);
  return {
    clientVersion: "1.0.0", game: "aion2", bossNpcName: "Enhanced Harcon", bossNpcId: 2300104, bossMaxHp: 1_000_000,
    startedAt: new Date(start).toISOString(), endedAt: new Date(start + 150_000).toISOString(), serverFingerprint: "aion2:europe-kaisinel", serverName: "Europe - Kaisinel",
    participants: names.map((n, i) => participant(n, i % 2 === 0 ? "Gladiator" : "Assassin", 600_000, i === 0)),
  };
}

const post = (payload: unknown) => app.inject({ method: "POST", url: "/api/uploads", payload: payload as object });
const setStatus = (name: string, status: string) => {
  const row = db.select().from(players).where(eq(players.name, name)).get()!;
  // 'client' = NC looked after his own client named his server (a due retry would be untried): nobody there is the failure every client >= 0.26 can get
  const values = { status, ncServerId: 1304, candidates: 0, checkedAt: 1, nextTryAt: Date.now() + 1e9 };
  db.insert(playerNcMatch).values({ playerId: row.id, ...values }).onConflictDoUpdate({ target: playerNcMatch.playerId, set: values }).run();
};

test("an upload with unchecked players waits; once NC has verified all of them it is merged", async () => {
  const response = await post(body(1, ["Aaa", "Bbb"]));
  assert.equal(response.statusCode, 200, response.body);
  assert.equal(response.json().status, "pending_review");
  assert.deepEqual(response.json().reasons, ["nc_pending"]);
  assert.equal(db.select().from(encounters).all().length, 0);

  setStatus("Aaa", "verified");
  assert.deepEqual(settleNcPending(), { merged: 0, failed: 0 }, "one player is still open");
  setStatus("Bbb", "verified");
  assert.deepEqual(settleNcPending(), { merged: 1, failed: 0 });
  assert.equal(db.select().from(encounters).all().length, 1);
});

test("one failed player fails the whole run, at once for known players and later for a waiting upload", async () => {
  // known players: Ccc verified, Ddd failed -> turned away at once
  const first = await post(body(2, ["Ccc", "Ddd"]));
  assert.equal(first.json().status, "pending_review");
  setStatus("Ccc", "verified");
  setStatus("Ddd", "unmatched");
  // a player seen for more than two days: NC knows nobody of his name, the run is turned away (a younger one waits, see the next test)
  db.update(players).set({ firstSeenAt: "2026-01-01 00:00:00" }).where(eq(players.name, "Ddd")).run();
  assert.deepEqual(settleNcPending(), { merged: 0, failed: 1 });
  assert.equal(db.select().from(encounters).all().length, 1, "no new run");

  const again = await post(body(3, ["Ccc", "Ddd"]));
  assert.equal(again.statusCode, 400);
  assert.equal(again.json().error, "player_check_failed");
  assert.equal(db.select().from(uploads).where(eq(uploads.status, "rejected")).all().length >= 2, true);

  // all verified -> merged at once
  const fine = await post(body(4, ["Aaa", "Bbb"]));
  assert.equal(fine.statusCode, 200, fine.body);
  assert.notEqual(fine.json().status, "pending_review");
});

test("a player first seen within two days, whom NC does not know yet (a renamed character), makes the run wait instead of failing it", async () => {
  const response = await post(body(5, ["Eee", "Fff"]));
  assert.equal(response.json().status, "pending_review");
  setStatus("Eee", "verified");
  setStatus("Fff", "unmatched"); // NC found nobody of that name - he was only just created
  assert.deepEqual(settleNcPending(), { merged: 0, failed: 0 }, "young: the run waits");
  assert.equal(db.select().from(encounters).all().length, 2, "no new run yet");

  setStatus("Fff", "verified"); // NC knows him by the next try
  assert.deepEqual(settleNcPending(), { merged: 1, failed: 0 });
  assert.equal(db.select().from(encounters).all().length, 3);
});

test("a new name that arrives with a game character id another row of the server holds asks NC about the old row at once", async () => {
  const withId = (day: number, names: string[], ids: Record<string, number>) => {
    const b = body(day, names);
    b.participants = b.participants.map((p) => ({ ...p, ...(ids[p.name] ? { characterId: ids[p.name] } : {}) }));
    return b;
  };
  await post(withId(6, ["Alt1", "Gg1"], { Alt1: 777001 }));
  const old = db.select().from(players).where(eq(players.name, "Alt1")).get()!;
  assert.equal(old.gameCharacterId, 777001);
  setStatus("Alt1", "verified"); // next try in a long time
  assert.notEqual(db.select().from(playerNcMatch).where(eq(playerNcMatch.playerId, old.id)).get()?.nextTryAt, 0);
  await post(withId(7, ["Neu1", "Gg1"], { Neu1: 777001 }));
  assert.equal(db.select().from(playerNcMatch).where(eq(playerNcMatch.playerId, old.id)).get()?.nextTryAt, 0, "the old row is due now");
  const fresh = db.select().from(players).where(eq(players.name, "Neu1")).get()!;
  assert.equal(fresh.gameCharacterId, 777001);
});
