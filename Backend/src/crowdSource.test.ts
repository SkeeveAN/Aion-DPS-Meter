import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-crowdsource-")), "test.sqlite");
process.env.CLIENT_UPDATE_ENFORCE = "off";
process.env.CROWD_LOOKUP = "on";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");
const { bossNpcIds, bosses, encounters, instances, uploads } = await import("./db/schema.js");
const { syncAbyssToDb, syncMatchmakingToDb } = await import("./serverMatchmakingDb.js");
const { CrowdSource, candidateServers, placeByFaction } = await import("./crowdResolve.js");
const { eq } = await import("drizzle-orm");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

syncMatchmakingToDb([{ region: "Europe", noticeId: "n1", pairs: [{ region: "Europe", elyos: "Kaisinel", asmodian: "Israphel", phase: null, updated: null }] }], 1000);
syncAbyssToDb([{ region: "Europe", noticeId: "n1", pairs: [{ region: "Europe", server1: "Ludra", server2: "Munin", faction1: "Asmodian", faction2: "Asmodian" }] }], 1000);
const instance = db.insert(instances).values({ name: "Verteron", nameEn: "Verteron", game: "aion2", slug: "verteron-y", category: "worldboss" }).returning().get();
const boss = db.insert(bosses).values({ instanceId: instance.id, name: "Gartuz", nameEn: "Gartuz", slug: "gartuz" }).returning().get();
// 2101074 is Eternal Gartua of Verteron in worldbosses.json, 2600068 Argo of the Abyss
db.insert(bossNpcIds).values({ bossId: boss.id, npcId: 2101074 }).run();
const abyssInstance = db.insert(instances).values({ name: "Abyss", nameEn: "Abyss", game: "aion2", slug: "abyss-y", category: "worldboss" }).returning().get();
const abyssBoss = db.insert(bosses).values({ instanceId: abyssInstance.id, name: "Argoz", nameEn: "Argoz", slug: "argoz" }).returning().get();
db.insert(bossNpcIds).values({ bossId: abyssBoss.id, npcId: 2600068 }).run();

const row = (name: string, className: string, damage: number, extra: object = {}) => ({
  name, className, faction: "", isSelf: false, totalDamage: damage, dps: damage / 1000, idps: damage / 1000, estimated: true,
  totalHealing: 0, hps: 0, damageTaken: 0, damageAbsorbed: 0, stagger: 0, shieldsGiven: [], buffs: [],
  skills: [{ skill: "Cleave", hits: 10, critHits: 0, total: damage, min: 1, max: 100 }], healSkills: [], ...extra,
});
// 2026-10-31 11:26 local = 10:26 UTC: a rift hour in Europe
const riftStart = Date.UTC(2026, 9, 31, 10, 26, 54);
const base = (extra: object, participants: object[]) => ({
  clientVersion: "1.0.0", game: "aion2" as const, mode: "", bossNpcName: "Gartuz", bossNpcId: 2101074, bossMaxHp: 1_000_000_000,
  startedAt: new Date(riftStart).toISOString(), endedAt: new Date(riftStart + 7_000).toISOString(), serverFingerprint: "aion2:europe-kaisinel", serverName: "Europe - Kaisinel",
  participants, ...extra,
});
const elyosSelf = row("Aahz", "Gladiator", 300_000_000, { faction: "Elyos", serverId: 1304, isSelf: true, estimated: false });

test("in a rift hour a player with a known faction is placed by the pairing (Elyos: Kaisinel, Asmodian: Israphel) without asking NC; one without faction stays open", () => {
  const payload = base({}, [elyosSelf, row("Bob", "Assassin", 100, { faction: "Elyos" }), row("Cara", "Cleric", 100, { faction: "Asmodian" }), row("Dan", "Ranger", 100)]);
  assert.deepEqual(candidateServers(payload as never).sort(), [1304, 2301]);
  assert.equal(placeByFaction(payload as never), 2);
  const by = Object.fromEntries(payload.participants.map((p) => [(p as { name: string }).name, p as { serverId?: number }]));
  assert.equal(by.Bob.serverId, 1304);
  assert.equal(by.Cara.serverId, 2301);
  assert.equal(by.Dan.serverId, undefined);
});

test("the Abyss uses the Abyss pairing: of two servers of one faction the faction does not tell, the other faction fits none - both stay for NC", () => {
  const payload = base({ bossNpcName: "Argoz", bossNpcId: 2600068, serverFingerprint: "aion2:europe-ludra", serverName: "Europe - Ludra" }, [
    row("Selma", "Gladiator", 300_000_000, { faction: "Asmodian", serverId: 2311, isSelf: true, estimated: false }),
    row("Ana", "Cleric", 100, { faction: "Asmodian" }),
    row("Eli", "Ranger", 100, { faction: "Elyos" }),
  ]);
  assert.deepEqual(candidateServers(payload as never).sort(), [2311, 2313], "Ludra and Munin, the Abyss pair");
  assert.equal(placeByFaction(payload as never), 0);
});

test("the workers take the players of a waiting upload one by one, in parallel, and the upload is filed when the last is placed", async () => {
  const names = Array.from({ length: 12 }, (_, i) => `Spieler${String.fromCharCode(65 + i)}`);
  const payload = base({}, [elyosSelf, ...names.map((n) => row(n, "Ranger", 80_000_000))]);
  const res = await app.inject({ method: "POST", url: "/api/uploads", payload });
  assert.deepEqual(res.json().reasons, ["crowd_pending"]);
  assert.equal(db.select().from(encounters).all().length, 0);

  let running = 0;
  let most = 0;
  let asked = 0;
  const nc = (async (input: unknown) => {
    const url = new URL(String(input));
    const keyword = url.searchParams.get("keyword") ?? "";
    const serverId = Number(url.searchParams.get("serverId"));
    asked++;
    running++;
    most = Math.max(most, running);
    await new Promise((r) => setTimeout(r, 15));
    running--;
    // every player exists once on Kaisinel (Elyos, Ranger = pcId 13)
    const list = serverId === 1304 ? [{ name: keyword, race: 1, serverId: 1304, pcId: 13, level: 45, characterId: `${keyword}x` }] : [];
    return new Response(JSON.stringify({ list, pagination: { page: 1, size: 40, total: list.length, endPage: 1 } }), { status: 200, headers: { "content-type": "application/json" } });
  }) as unknown as typeof fetch;

  const source = new CrowdSource(app.log, nc);
  source.refresh();
  assert.ok(source.size > 0, "the waiting upload counts as work");
  // four workers, as the PortraitQueue starts them
  const worker = async () => {
    for (;;) {
      const unit = source.take();
      if (unit === undefined) {
        if (source.size === 0) return;
        await new Promise((r) => setTimeout(r, 5));
        continue;
      }
      await source.run(unit);
    }
  };
  await Promise.all([worker(), worker(), worker(), worker()]);
  // the last run files the upload; give it a moment to finish
  for (let i = 0; i < 50 && db.select().from(encounters).all().length === 0; i++) {
    await new Promise((r) => setTimeout(r, 20));
  }
  assert.ok(most >= 2, `the lookups ran side by side (${most})`);
  assert.equal(asked, 24, "two servers asked per player, nothing twice");
  assert.equal(db.select().from(encounters).all().length, 1);
  assert.equal(db.select().from(uploads).where(eq(uploads.status, "pending")).all().length, 0);
});
