import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-partner-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { partnerRoutes } = await import("./routes/partner.js");
const keys = await import("./partnerKeys.js");
const api = await import("./partnerApi.js");

const app = Fastify({ logger: false });
await app.register(partnerRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

const { key } = keys.createKey("test partner", 1000);
const get = (url: string, k: string | null = key) => app.inject({ method: "GET", url, headers: k === null ? {} : { "x-api-key": k } });

test("without a key, or with a wrong one, nothing is answered", async () => {
  assert.equal((await get("/api/partner/v1/pets", null)).statusCode, 401);
  assert.equal((await get("/api/partner/v1/pets", "adps_wrong")).statusCode, 401);
});

test("only the hash of a key is stored, and a revoked key stops working", async () => {
  const made = keys.createKey("to revoke");
  const stored = JSON.stringify(keys.listKeys());
  assert.ok(!stored.includes(made.key));
  assert.equal((await get("/api/partner/v1/events", made.key)).statusCode, 200);
  assert.ok(keys.revokeKey(made.id));
  assert.equal((await get("/api/partner/v1/events", made.key)).statusCode, 401);
});

test("English is the default language and German can be asked for", async () => {
  const en = (await get("/api/partner/v1/events")).json();
  const de = (await get("/api/partner/v1/events?lang=de")).json();
  assert.equal(en.lang, "en");
  assert.equal(de.lang, "de");
  assert.equal(en.events.length, de.events.length);
  assert.ok(en.events.some((e: { id: string; name: string }, i: number) => e.name !== de.events[i].name), "at least one event has a German name that differs");
  const bad = await get("/api/partner/v1/events?lang=xx");
  assert.equal(bad.statusCode, 400);
  assert.ok(bad.json().languages.includes("de"));
});

test("pets come with regions, and one pet with its monsters and places", async () => {
  const list = (await get("/api/partner/v1/pets?lang=de")).json();
  assert.ok(list.count > 100);
  const withRegion = list.items.find((p: { regions: unknown[] }) => p.regions.length > 0);
  assert.ok(withRegion);
  const detail = (await get(`/api/partner/v1/pets/${withRegion.id}?lang=de`)).json();
  assert.equal(detail.id, withRegion.id);
  assert.ok(Array.isArray(detail.monsters) && Array.isArray(detail.spawns));
  assert.equal((await get("/api/partner/v1/pets/abc")).statusCode, 404);
  assert.equal((await get("/api/partner/v1/pets/999999999")).statusCode, 404);
});

test("the static data lists answer", async () => {
  for (const r of ["classes", "instances", "bosses", "worldbosses", "maps", "titles", "wings"]) {
    const res = await get(`/api/partner/v1/${r}?lang=de`);
    assert.equal(res.statusCode, 200, r);
    assert.ok(res.json().count > 0, r);
  }
  const instances = (await get("/api/partner/v1/instances")).json().items;
  const bosses = (await get(`/api/partner/v1/bosses?instance=${instances[0].slug}`)).json().items;
  assert.ok(bosses.every((b: { instance: string }) => b.instance === instances[0].slug));
});

test("a language falls back to English where the game has no name", () => {
  assert.equal(api.pick({ en: "A" }, "de"), "A");
  assert.equal(api.pick({ en: "A", de: "B" }, "de"), "B");
  assert.equal(api.parseLang(undefined), "en");
  assert.equal(api.parseLang("de-DE"), "de");
  assert.equal(api.parseLang("xx"), null);
});

test("a key over its limit gets 429 with the time to wait", async () => {
  keys.resetRateLimits();
  const limited = keys.createKey("limited", 2).key;
  assert.equal((await get("/api/partner/v1/classes", limited)).statusCode, 200);
  assert.equal((await get("/api/partner/v1/classes", limited)).statusCode, 200);
  const third = await get("/api/partner/v1/classes", limited);
  assert.equal(third.statusCode, 429);
  assert.ok(Number(third.headers["retry-after"]) >= 1);
});
