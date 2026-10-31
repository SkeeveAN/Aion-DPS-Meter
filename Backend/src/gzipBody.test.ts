import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-gzip-")), "test.sqlite");
process.env.CLIENT_UPDATE_ENFORCE = "off";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { registerGzipBodies } = await import("./gzipBody.js");
const { bossNpcIds, bosses, instances } = await import("./db/schema.js");

const app = Fastify({ logger: false });
registerGzipBodies(app);
await app.register(uploadRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

const instance = db.insert(instances).values({ name: "Zip place", nameEn: "Zip place", game: "aion2", slug: "zip-place", category: "expedition" }).returning().get();
const boss = db.insert(bosses).values({ instanceId: instance.id, name: "Zipper", nameEn: "Zipper", slug: "zipper" }).returning().get();
db.insert(bossNpcIds).values({ bossId: boss.id, npcId: 9300001 }).run();

const fight = (offsetMs: number) => {
  const start = Date.UTC(2026, 0, 5, 20, 0, 0) + offsetMs;
  return {
    clientVersion: "1.0.0", game: "aion2" as const, mode: "", bossNpcName: "Zipper", bossNpcId: 9300001, bossMaxHp: 1_000_000,
    startedAt: new Date(start).toISOString(), endedAt: new Date(start + 100_000).toISOString(), serverFingerprint: "aion2:europe-kaisinel",
    participants: [{
      name: "Aahz", className: "Gladiator", faction: "Elyos", serverId: 1304, isSelf: true, totalDamage: 1_000_000, dps: 10_000, idps: 10_000, estimated: false,
      totalHealing: 0, hps: 0, damageTaken: 0, damageAbsorbed: 0, stagger: 0, shieldsGiven: [], buffs: [],
      skills: [{ skill: "Cleave", hits: 100, critHits: 0, total: 1_000_000, min: 1, max: 100 }], healSkills: [],
    }],
  };
};

const zipped = (body: unknown) => ({ method: "POST" as const, url: "/api/uploads", headers: { "content-type": "application/json", "content-encoding": "gzip" }, payload: gzipSync(Buffer.from(JSON.stringify(body))) });

test("a gzip-compressed upload is unpacked and filed like a plain one", async () => {
  const plain = await app.inject({ method: "POST", url: "/api/uploads", payload: fight(0) });
  assert.equal(plain.statusCode, 200);
  const packed = await app.inject(zipped(fight(3_600_000)));
  assert.equal(packed.statusCode, 200);
  assert.ok(packed.json().encounterId > 0);
  assert.notEqual(packed.json().encounterId, plain.json().encounterId);
});

test("a body that is not valid gzip is turned away, not filed", async () => {
  const res = await app.inject({ method: "POST", url: "/api/uploads", headers: { "content-type": "application/json", "content-encoding": "gzip" }, payload: Buffer.from("this is not gzip data") });
  assert.ok(res.statusCode >= 400 && res.statusCode < 500, `status ${res.statusCode}`);
});

test("the body limit counts the unpacked bytes (a small zip of a huge body is refused)", async () => {
  const huge = Buffer.from(`{"pad":"${"a".repeat(41 * 1024 * 1024)}"}`);
  const res = await app.inject({ method: "POST", url: "/api/uploads", headers: { "content-type": "application/json", "content-encoding": "gzip" }, payload: gzipSync(huge) });
  assert.equal(res.statusCode, 413);
});
