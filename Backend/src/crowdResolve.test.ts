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
const { settleCrowdPending, candidateServers, riftOpen, dropServersOutsideRule, resolveName } = await import("./crowdResolve.js");
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

test("without a region a rift is the hour after each of 00:00, 03:00 ... 21:00 UTC (with two minutes of slack)", () => {
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

test("in Europe the rifts are at 02:00, 05:00 ... 23:00 local time, so an hour later in UTC after the clocks went back (25.10.2026)", () => {
  const at = (day: number, h: number, m: number) => Date.UTC(2026, 9, day, h, m, 0);
  const open = (t: number) => riftOpen(t, t + 12_000, "Europe");
  // summer time (UTC+2): 14:00-15:00 local = 12:00-13:00 UTC, 17:00 = 15:00 UTC
  assert.equal(open(at(20, 12, 30)), true, "20.10. 14:30 local");
  assert.equal(open(at(20, 13, 30)), false, "20.10. 15:30 local");
  assert.equal(open(at(20, 15, 30)), true, "20.10. 17:30 local");
  // winter time (UTC+1): 14:00 local = 13:00 UTC, 17:00 = 16:00 UTC, 20:00 = 19:00 UTC
  assert.equal(open(at(26, 13, 30)), true, "26.10. 14:30 local");
  assert.equal(open(at(26, 12, 30)), false, "26.10. 13:30 local");
  assert.equal(open(at(26, 16, 3)), true, "26.10. 17:03 local (the Kasaka kill with 23 Asmodians of Zemurru, 25.10.)");
  assert.equal(open(at(26, 16, 10)), true, "17:10 local");
  assert.equal(open(at(26, 15, 30)), false, "26.10. 16:30 local");
  assert.equal(open(at(26, 17, 30)), false, "26.10. 18:30 local: the hour is over");
  // other regions keep the UTC hours until their zone is known
  assert.equal(riftOpen(at(26, 12, 30), at(26, 12, 30) + 12_000, "Asia"), true);
  assert.equal(riftOpen(at(26, 13, 30), at(26, 13, 30) + 12_000, "Asia"), false);
});

test("a server the client names for a player of the crowd outside the rule is taken from him, and he is placed like a player the client knows nothing of", () => {
  const filed = (startedAt: number, servers: Record<string, number>) => {
    const copy = JSON.parse(JSON.stringify(payload));
    copy.bossNpcId = 2100003; // a Verteron field boss: Elyos home map
    copy.startedAt = new Date(startedAt).toISOString();
    copy.endedAt = new Date(startedAt + 12_000).toISOString();
    for (const p of copy.participants) {
      if (servers[p.name] !== undefined) {
        p.serverId = servers[p.name];
        p.faction = servers[p.name] < 2000 ? "Elyos" : "Asmodian";
      }
    }
    return copy;
  };
  const names = (copy: { participants: { name: string; serverId?: number }[] }) => Object.fromEntries(copy.participants.map((p) => [p.name, p.serverId]));
  // 20:30 UTC = 21:30 local in winter: no rift, only the uploader's own server is on the map of a home-map field boss
  const quiet = filed(Date.UTC(2026, 0, 5, 20, 30, 0), { Bob: 1311, Cara: 2304, Dan: 1304 });
  assert.equal(dropServersOutsideRule(quiet), 2);
  assert.deepEqual([names(quiet).Bob, names(quiet).Cara, names(quiet).Dan], [undefined, undefined, 1304]);
  assert.equal(quiet.participants.find((p: { name: string }) => p.name === "Bob").faction, "", "faction goes with the server");
  // 19:30 UTC = 20:30 local: a rift, the paired server (Lumiel 2304) can be there, a third one (Kaisinel's neighbour 1311) never
  const rift = filed(Date.UTC(2026, 0, 5, 19, 30, 0), { Bob: 1311, Cara: 2304, Dan: 1304 });
  assert.equal(dropServersOutsideRule(rift), 1);
  assert.deepEqual([names(rift).Bob, names(rift).Cara, names(rift).Dan], [undefined, 2304, 1304]);
  // the uploader himself is never touched
  assert.equal(names(rift).Anna, 1304);
});

test("a common name is looked up on each candidate server on its own, so the hits past the pages read cannot hide him", async () => {
  // NC lists 175 characters for "Pixie" on five pages; the unfiltered search reads three of them. Asked for one server it answers that server's characters only.
  const crowd = Array.from({ length: 120 }, (_, i) => ({ name: `Pixie${i}`, race: 1, serverId: 1310 + (i % 5), pcId: 13, level: 45, characterId: `x${i}` }));
  const ncFiltered = (async (input: unknown) => {
    const q = new URL(String(input)).searchParams;
    const server = Number(q.get("serverId") ?? 0);
    const list = server === 0 ? crowd : server === 2304 ? [{ name: "Pixie", race: 2, serverId: 2304, pcId: 13, level: 45, characterId: "real" }] : [];
    return new Response(JSON.stringify({ list, pagination: { page: 1, size: 40, total: list.length, endPage: 1 } }), { status: 200, headers: { "content-type": "application/json" } });
  }) as unknown as typeof fetch;
  const verdict = await resolveName(ncFiltered, "Pixie", "Ranger", [1304, 2304], "Europe - Kaisinel");
  assert.deepEqual(verdict, { serverId: 2304, faction: "Asmodian" });
});
