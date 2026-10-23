import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-review-routes-")), "test.sqlite");
process.env.UPLOAD_REVIEW = "on";
process.env.ADMIN_TOKEN = "secret-token-for-the-test";
process.env.CLIENT_UPDATE_ENFORCE = "off";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { adminUploadRoutes } = await import("./routes/adminUploads.js");
const { uploads, encounters } = await import("./db/schema.js");
const { processUpload } = await import("./matching/merge.js");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.register(adminUploadRoutes);
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

function body(day: number, seconds: number, damages: number[] = [600_000, 500_000]) {
  const start = Date.UTC(2026, 0, day, 20, 0, 0);
  return {
    clientVersion: "1.0.0", game: "aion2", bossNpcName: "Enhanced Harcon", bossNpcId: 2300104, bossMaxHp: 1_000_000,
    startedAt: new Date(start).toISOString(), endedAt: new Date(start + seconds * 1000).toISOString(), serverFingerprint: "aion2:europe-kaisinel",
    participants: damages.map((d, i) => participant(`Player${i}`, i % 2 === 0 ? "Gladiator" : "Assassin", d, i === 0)),
  };
}

const post = (payload: unknown) => app.inject({ method: "POST", url: "/api/uploads", payload: payload as object });
const admin = { authorization: "Bearer secret-token-for-the-test" };

test("a boss nobody has catalogued waits for review; once it is on file, ordinary kills are merged at once and the first ones are never judged by comparison", async () => {
  const unknown = await post(body(1, 150));
  assert.equal(unknown.statusCode, 200, unknown.body);
  assert.equal(unknown.json().status, "pending_review");
  assert.deepEqual(unknown.json().reasons, ["unknown_boss"]);
  assert.equal(db.select().from(encounters).all().length, 0);

  // approved by hand once (here: put on file directly), the boss is known from then on
  processUpload(body(1, 150) as unknown as Parameters<typeof processUpload>[0]);
  for (let day = 2; day <= 3; day++) {
    const response = await post(body(day, 150));
    assert.equal(response.statusCode, 200, response.body);
    assert.notEqual(response.json().status, "pending_review");
  }

  assert.equal(db.select().from(encounters).all().length, 3);
});

test("a fight whose end is before its start is turned away and kept in the audit log with the reason", async () => {
  const bad = body(4, 150);
  bad.endedAt = new Date(Date.parse(bad.startedAt) - 60_000).toISOString();
  const response = await post(bad);
  assert.equal(response.statusCode, 400);
  assert.equal(response.json().error, "implausible_upload");
  const row = db.select().from(uploads).all().find((u) => u.status === "rejected" && (u.reviewReason ?? "").includes("end_before_start"));
  assert.ok(row);
});

test("a kill much faster than the fastest known one waits for review and stays off the leaderboards", async () => {
  const before = db.select().from(encounters).all().length;
  const response = await post(body(5, 20));
  assert.equal(response.statusCode, 200, response.body);
  assert.equal(response.json().status, "pending_review");
  assert.ok(response.json().reasons.includes("too_fast"));
  assert.equal(db.select().from(encounters).all().length, before);
});

test("the review queue answers only with the token, lists what waits and approves or rejects it", async () => {
  assert.equal((await app.inject({ method: "GET", url: "/api/admin/uploads/pending" })).statusCode, 404);
  assert.equal((await app.inject({ method: "GET", url: "/api/admin/uploads/pending", headers: { authorization: "Bearer wrong" } })).statusCode, 404);
  const list = await app.inject({ method: "GET", url: "/api/admin/uploads/pending", headers: admin });
  assert.equal(list.statusCode, 200);
  const waiting = list.json().pending as { id: number; reasons: { code: string }[] }[];
  assert.equal(waiting.length, 2); // the boss nobody had catalogued, and the kill that was too fast
  const pending = waiting.filter((p) => p.reasons.some((r) => r.code === "too_fast"));
  assert.equal(pending.length, 1);

  const before = db.select().from(encounters).all().length;
  const approved = await app.inject({ method: "POST", url: `/api/admin/uploads/${pending[0]!.id}/approve`, headers: admin });
  assert.equal(approved.statusCode, 200, approved.body);
  assert.equal(db.select().from(encounters).all().length, before + 1);
  assert.equal((await app.inject({ method: "POST", url: `/api/admin/uploads/${pending[0]!.id}/approve`, headers: admin })).statusCode, 404);

  await post(body(6, 5)); // another odd one (faster than the kill that was just approved)
  const second = ((await app.inject({ method: "GET", url: "/api/admin/uploads/pending", headers: admin })).json().pending as { id: number; reasons: { code: string }[] }[]).find((p) => p.reasons.some((r) => r.code === "too_fast"))!;
  assert.equal((await app.inject({ method: "POST", url: `/api/admin/uploads/${second.id}/reject`, headers: admin })).statusCode, 200);
  assert.equal((await app.inject({ method: "GET", url: "/api/admin/uploads/pending", headers: admin })).json().pending.length, 1); // the first one is still waiting
});

test("top rule: a player without name, server or faction is left out; the uploader's own row has to be complete", async () => {
  const complete = body(10, 150);
  const withGaps = {
    ...complete,
    participants: [
      ...complete.participants,
      { ...participant("NoServer", "Gladiator", 1000), serverId: undefined },
      { ...participant("NoFaction", "Gladiator", 1000), faction: "" },
    ],
  };
  const accepted = await post(withGaps);
  assert.equal(accepted.statusCode, 200, accepted.body);
  const { players } = await import("./db/schema.js");
  const names = db.select().from(players).all().map((p) => p.name);
  assert.ok(!names.includes("NoServer") && !names.includes("NoFaction"));

  const selfGap = { ...complete, participants: complete.participants.map((p) => (p.isSelf ? { ...p, faction: "" } : p)) };
  const refused = await post(selfGap);
  assert.equal(refused.statusCode, 400);
  assert.equal(refused.json().error, "player_incomplete");
});

test("the very same payload from the same uploader is taken once and not filed again; a changed one is merged as before", async () => {
  const same = body(20, 150);
  const first = await post(same);
  assert.equal(first.statusCode, 200, first.body);
  const filed = db.select().from(uploads).all().length;
  const again = await post(same);
  assert.equal(again.statusCode, 200, again.body);
  assert.equal(again.json().status, "duplicate");
  assert.equal(db.select().from(uploads).all().length, filed, "the second one leaves no row");
  const changed = await post(body(20, 151));
  assert.equal(changed.statusCode, 200, changed.body);
  assert.notEqual(changed.json().status, "duplicate");
});
