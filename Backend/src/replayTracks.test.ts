import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-replay-")), "test.sqlite");
process.env.CLIENT_UPDATE_ENFORCE = "off";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { bossNpcIds, bosses, instances } = await import("./db/schema.js");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

const instance = db.insert(instances).values({ name: "Replay place", nameEn: "Replay place", game: "aion2", slug: "replay-place", category: "expedition" }).returning().get();
const boss = db.insert(bosses).values({ instanceId: instance.id, name: "Replayer", nameEn: "Replayer", slug: "replayer" }).returning().get();
db.insert(bossNpcIds).values({ bossId: boss.id, npcId: 9200001 }).run();

const START = Date.UTC(2026, 0, 5, 20, 0, 0);
const fight = {
  clientVersion: "1.0.0", game: "aion2" as const, mode: "", bossNpcName: "Replayer", bossNpcId: 9200001, bossMaxHp: 1_000_000,
  startedAt: new Date(START).toISOString(), endedAt: new Date(START + 100_000).toISOString(), serverFingerprint: "aion2:europe-kaisinel",
  participants: [{
    name: "Aahz", className: "Gladiator", faction: "Elyos", serverId: 1304, isSelf: true, totalDamage: 1_000_000, dps: 10_000, idps: 10_000, estimated: false,
    totalHealing: 0, hps: 0, damageTaken: 0, damageAbsorbed: 0, stagger: 0, shieldsGiven: [], buffs: [],
    skills: [{ skill: "Cleave", hits: 100, critHits: 0, total: 1_000_000, min: 1, max: 100 }], healSkills: [],
  }],
};

const track = (name: string, kind: "player" | "boss" | "add", startOffsetMs: number, t: number[], x: number[], isSelf = false) => ({
  kind, name, isSelf, className: kind === "player" ? "Gladiator" : undefined, startedAt: new Date(START + startOffsetMs).toISOString(), t, x, y: x.map(() => 100),
});

test("the paths of the clients of a fight are kept per uploader and merged when fetched; a fight the server does not know is turned away", async () => {
  const created = await app.inject({ method: "POST", url: "/api/uploads", payload: fight });
  assert.equal(created.statusCode, 200);
  const encounterId = created.json().encounterId as number;
  assert.ok(encounterId > 0);

  // client one: its own path (points 0, 5, 10 s) and the boss
  const one = await app.inject({
    method: "POST", url: "/api/uploads/replay", remoteAddress: "10.0.0.1",
    payload: { clientVersion: "1.0.0", game: "aion2", serverFingerprint: fight.serverFingerprint, encounterId, startedAt: fight.startedAt, endedAt: fight.endedAt,
      tracks: [track("Aahz", "player", 0, [0, 5000, 10000], [0, 500, 1000], true), track("Replayer", "boss", 0, [0, 10000], [300, 300])] },
  });
  assert.equal(one.statusCode, 200);
  assert.equal(one.json().tracks, 2);

  // client two saw Aahz walk in between (at 2.5 s and 7.5 s, one reading shared with client one at 5.1 s) and an add
  const two = await app.inject({
    method: "POST", url: "/api/uploads/replay", remoteAddress: "10.0.0.2",
    payload: { clientVersion: "1.0.0", game: "aion2", serverFingerprint: fight.serverFingerprint, bossNpcId: 9200001, startedAt: fight.startedAt, endedAt: fight.endedAt,
      tracks: [track("Aahz", "player", 2500, [0, 2600, 5000], [250, 510, 750]), track("Imp", "add", 0, [0], [700])] },
  });
  assert.equal(two.statusCode, 200, "found by boss and start time without the fight's id");

  const merged = (await app.inject({ method: "GET", url: `/api/replay?server=${encodeURIComponent(fight.serverFingerprint)}&encounterId=${encounterId}` })).json() as {
    encounterId: number;
    tracks: { kind: string; name: string; startMs: number; t: number[]; x: number[]; sources: number }[];
  };
  assert.equal(merged.encounterId, encounterId);
  const aahz = merged.tracks.find((t) => t.name === "Aahz")!;
  assert.equal(aahz.sources, 2);
  // 0, 2.5, 5.0 (client one), 5.1 (client two: the same reading), 7.5, 10 s: the reading 0.1 s after another is dropped, the rest is in time order
  assert.deepEqual(aahz.t.map((v) => Math.round(v / 100) / 10), [0, 2.5, 5, 7.5, 10]);
  assert.deepEqual(aahz.x, [0, 250, 500, 750, 1000]);
  assert.deepEqual(merged.tracks.map((t) => t.kind + ":" + t.name).sort(), ["add:Imp", "boss:Replayer", "player:Aahz"]);

  // the same fight found by boss and start time only
  const byTime = (await app.inject({ method: "GET", url: `/api/replay?server=${encodeURIComponent(fight.serverFingerprint)}&bossNpcId=9200001&start=${encodeURIComponent(new Date(START + 3000).toISOString())}` })).json();
  assert.equal(byTime.encounterId, encounterId);

  // an unknown fight
  const none = await app.inject({
    method: "POST", url: "/api/uploads/replay",
    payload: { clientVersion: "1.0.0", game: "aion2", serverFingerprint: fight.serverFingerprint, bossNpcId: 9200001, startedAt: new Date(START + 6 * 3600_000).toISOString(), endedAt: new Date(START + 6 * 3600_000 + 1000).toISOString(),
      tracks: [track("Aahz", "player", 0, [0], [0])] },
  });
  assert.equal(none.statusCode, 404);
  const bad = await app.inject({ method: "POST", url: "/api/uploads/replay", payload: { game: "aion2", serverFingerprint: "x", startedAt: "", endedAt: "", tracks: [{ kind: "player", name: "A", startedAt: "", t: [0, 1], x: [1], y: [1, 2] }] } });
  assert.equal(bad.statusCode, 400);
});
