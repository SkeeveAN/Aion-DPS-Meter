import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { gunzipSync } from "node:zlib";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-recordings-")), "test.sqlite");
process.env.CLIENT_UPDATE_ENFORCE = "off";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { statRecordingRoutes } = await import("./routes/statRecordings.js");
const { networkReportRoutes } = await import("./routes/networkReports.js");
const { statRecordings, networkReports } = await import("./db/schema.js");

const app = Fastify({ logger: false });
await app.register(statRecordingRoutes);
await app.register(networkReportRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

const anon = "0123456789abcdef0123456789abcdef";

const recording = () => ({
  kind: "stat-recording", formatVersion: 1, clientVersion: "1.7.0", anonymousId: anon, mode: "expert",
  startedAt: "2026-10-19T18:00:00Z", endedAt: "2026-10-19T18:04:00Z", durationSeconds: 240,
  character: { classId: 1, level: 45, serverId: 1304, faction: "Elyos" },
  statsStart: { "19": 277, "317": 1050, "425": 337 }, statsEnd: { "19": 277, "317": 1050, "425": 337, "28": 500 },
  statChanges: [{ at: 12, id: 28, value: 500 }],
  hits: { total: 120, crit: 36 },
});

test("a stat recording is stored whole, gzipped, with its sorting columns and no link to anything", async () => {
  const res = await app.inject({ method: "POST", url: "/api/uploads/stat-recording", payload: recording() });
  assert.equal(res.statusCode, 200);
  const rows = db.select().from(statRecordings).all();
  assert.equal(rows.length, 1);
  assert.equal(rows[0].anonId, anon);
  assert.equal(rows[0].classId, 1);
  assert.equal(rows[0].level, 45);
  assert.equal(rows[0].serverId, 1304);
  assert.equal(rows[0].faction, "Elyos");
  assert.equal(rows[0].mode, "expert");
  assert.equal(rows[0].durationSeconds, 240);
  const stored = JSON.parse(gunzipSync(rows[0].payload).toString());
  assert.deepEqual(stored.statsEnd, { "19": 277, "317": 1050, "425": 337, "28": 500 });
  assert.deepEqual(stored.hits, { total: 120, crit: 36 });
});

test("a recording with a name, a legion or a wrong shape is turned away unread", async () => {
  const named = await app.inject({ method: "POST", url: "/api/uploads/stat-recording", payload: { ...recording(), characterName: "Aahz" } });
  assert.equal(named.statusCode, 400);
  const guild = await app.inject({ method: "POST", url: "/api/uploads/stat-recording", payload: { ...recording(), guild: "Akatsuki" } });
  assert.equal(guild.statusCode, 400);
  const nameInCharacter = await app.inject({ method: "POST", url: "/api/uploads/stat-recording", payload: { ...recording(), character: { ...recording().character, name: "Aahz" } } });
  assert.equal(nameInCharacter.statusCode, 400);
  const badId = await app.inject({ method: "POST", url: "/api/uploads/stat-recording", payload: { ...recording(), anonymousId: "Aahz" } });
  assert.equal(badId.statusCode, 400);
  const badStat = await app.inject({ method: "POST", url: "/api/uploads/stat-recording", payload: { ...recording(), statsStart: { abc: 5 } } });
  assert.equal(badStat.statusCode, 400);
  assert.equal(db.select().from(statRecordings).all().length, 1);
});

const report = () => ({
  format: "aiondps-network-report", formatVersion: 1, clientVersion: "1.7.0", anonymousId: anon, periodSeconds: 900,
  adapter: { type: "Ethernet", speedMbit: 1000 },
  stalls: [{ at: 30, seconds: 4.2, cause: "line" }],
  timeline: { bucketSeconds: 5, rows: [{ at: 0, pingMs: 41 }, { at: 5, pingMs: 43 }] },
});

test("a network report is stored with its sorting columns", async () => {
  const res = await app.inject({ method: "POST", url: "/api/diagnostics/network", payload: report() });
  assert.equal(res.statusCode, 200);
  const rows = db.select().from(networkReports).all();
  assert.equal(rows.length, 1);
  assert.equal(rows[0].periodSeconds, 900);
  assert.equal(rows[0].stallCount, 1);
  assert.equal(JSON.parse(gunzipSync(rows[0].payload).toString()).adapter.type, "Ethernet");
});

test("a network report in another shape is turned away", async () => {
  const other = await app.inject({ method: "POST", url: "/api/diagnostics/network", payload: { ...report(), format: "something" } });
  assert.equal(other.statusCode, 400);
  const noAnon = await app.inject({ method: "POST", url: "/api/diagnostics/network", payload: { ...report(), anonymousId: "" } });
  assert.equal(noAnon.statusCode, 400);
  assert.equal(db.select().from(networkReports).all().length, 1);
});
