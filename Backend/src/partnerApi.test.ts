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

test("a boss comes with its portrait and the loot of the possible-reward window", async () => {
  const list = (await get("/api/partner/v1/bosses?lang=de")).json().items;
  assert.ok(list.some((b: { portrait: string | null }) => b.portrait?.endsWith(".webp")));
  const detail = (await get("/api/partner/v1/bosses/kromedes-desire?lang=de")).json();
  assert.ok(detail.portrait.includes("/bosses/portraits/"));
  const easy = detail.loot.find((l: { mode: string; role: string }) => l.mode === "explore" && l.role === "final");
  assert.ok(easy);
  assert.deepEqual(easy.rolls.map((r: { chance: number; times: number }) => [r.chance, r.times]), [[7.5, 1], [100, 1], [100, 3], [60, 1]]);
  const unique = easy.rolls[0];
  assert.equal(unique.gradeName, "Einzigartig");
  assert.equal(unique.items.length, 8);
  assert.ok(unique.items.every((i: { chance: number }) => i.chance === 12.5));
  assert.equal(unique.items[0].name, "Kromedes Zweihandschwert");
  // the three steps are three bosses of their own and drop different gear: exploration low level pieces, conquest the level 86 ones
  const top = async (slug: string, mode: string) => {
    const one = (await get(`/api/partner/v1/bosses/${slug}?lang=de`)).json();
    assert.deepEqual(one.loot.map((l: { mode: string }) => l.mode), [mode]);
    return { modeName: one.loot[0].modeName, level: Math.max(...one.loot[0].rolls.flatMap((r: { items: { itemLevel: number | null }[] }) => r.items.map((i) => i.itemLevel ?? 0))) };
  };
  const explore = await top("kromedes-desire", "explore");
  assert.equal(explore.modeName, "Erkundung");
  assert.ok(explore.level < 50);
  assert.equal((await top("kromedes-desire-conquest", "conquest")).level, 86);
  assert.equal((await top("kromedes-desire-conquest-hard", "conquest-hard")).level, 86);
  assert.equal((await get("/api/partner/v1/bosses/does-not-exist")).statusCode, 404);
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

test("an item answers like its in-game tooltip, in the language asked for", async () => {
  const res = await get("/api/partner/v1/items/110130033?lang=de");
  assert.equal(res.statusCode, 200);
  const body = res.json();
  assert.equal(body.name, "Kromedes Zweihandschwert");
  assert.equal(body.requiredLevel, 37);
  assert.equal(body.manaStoneSlots, 4);
  assert.equal(body.soulBind.firstBindStats, 3);
  const speed = body.soulBind.possibleStats.find((s: { stat: string }) => s.stat === "CombatSpeed");
  assert.deepEqual([speed.unit, speed.min, speed.max], ["percent", 8.2, 9.5]);
  assert.ok(body.mainStats.some((s: { stat: string; value: number }) => s.stat === "WeaponDamage" && s.value === 251));
  assert.equal(body.labels.soulBindRate, "Seelenbindungsrate");
  assert.equal((await get("/api/partner/v1/items/1")).statusCode, 404);
  assert.equal((await get("/api/partner/v1/items/110130033?lang=xx")).statusCode, 400);
});

test("the bosses of an instance come with their loot in one request", async () => {
  const plain = (await get("/api/partner/v1/bosses?instance=ferocious-horn-den")).json();
  const withLoot = (await get("/api/partner/v1/bosses?instance=ferocious-horn-den&loot=1")).json();
  assert.equal(withLoot.count, plain.count);
  assert.ok(plain.items.every((b: { loot?: unknown }) => b.loot === undefined));
  const first = withLoot.items.find((b: { hasLoot: boolean }) => b.hasLoot);
  assert.ok(first.loot.length > 0 && first.loot[0].rolls[0].items[0].itemId > 0);
});

test("the OpenAPI description is served without a key", async () => {
  const res = await get("/api/partner/v1/openapi.json", null);
  assert.equal(res.statusCode, 200);
  assert.ok(res.json().paths["/api/partner/v1/items/{id}"]);
});
