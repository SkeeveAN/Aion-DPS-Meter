import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-crowdrule-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { bosses, encounterParticipants, encounters, instances, playerNcCharacter, playerNcMatch, players, uploads } = await import("./db/schema.js");
const { syncMatchmakingToDb } = await import("./serverMatchmakingDb.js");
const { serverRowOf } = await import("./ncMatch.js");
const { allowedServersOfEncounter, partnerServerAt, plausibleServersOf } = await import("./crowdRule.js");
const { cleanUpCrowdServers, findCrowdViolators } = await import("./crowdCleanup.js");
const { eq } = await import("drizzle-orm");

// Europe: Bakarma (Elyos 1316) is paired with Kromede (Asmodian 2316) from the start of the test clock on.
syncMatchmakingToDb([{ region: "Europe", noticeId: "n1", pairs: [{ region: "Europe", elyos: "Bakarma", asmodian: "Kromede", phase: null, updated: null }] }], 1000);

const altgard = db.insert(instances).values({ name: "Altgard", nameEn: "Altgard", game: "aion2", slug: "altgard-x", category: "worldboss" }).returning().get();
const abyss = db.insert(instances).values({ name: "Abyss", nameEn: "Abyss", game: "aion2", slug: "abyss-x", category: "worldboss" }).returning().get();
const gartua = db.insert(bosses).values({ instanceId: altgard.id, name: "Gartua", nameEn: "Gartua", slug: "gartua" }).returning().get();
const nahma = db.insert(bosses).values({ instanceId: abyss.id, name: "Nahma", nameEn: "Nahma", slug: "nahma" }).returning().get();

const KROMEDE = 2316;
const MUNIN = 2313;
const BAKARMA = 1316;
const kromedeRow = serverRowOf(KROMEDE);
const muninRow = serverRowOf(MUNIN);

// 2026-10-20: 14:06 UTC is no rift hour (14 % 3 = 2), 12:06 is one
const fight = (bossId: number, startedAt: string, uploaderServer: number) => {
  const e = db.insert(encounters).values({ serverId: kromedeRow, bossId, startedAt, endedAt: new Date(Date.parse(startedAt) + 30_000).toISOString(), durationSeconds: 30, groupIDps: 1, rosterFingerprint: "x" }).returning().get();
  db.insert(uploads).values({ uploaderReportedName: "Up", ipHash: "h", status: "merged", rawPayloadJson: "{}", clientServerId: uploaderServer, matchedEncounterId: e.id }).run();
  return e.id;
};
const player = (name: string, serverRow: number, faction: string) => db.insert(players).values({ name, nameNormalized: name.toLowerCase(), serverId: serverRow, faction }).returning().get().id;
const join = (encounterId: number, playerId: number) =>
  db.insert(encounterParticipants).values({ encounterId, playerId, className: "Ranger", faction: "Asmodian", totalDamage: 1, dps: 1, idps: 1, critRatePercent: 0 }).run();

const home = fight(gartua.id, "2026-10-20T14:06:00.000Z", KROMEDE); // a home map outside a rift hour: only Kromede
const rift = fight(gartua.id, "2026-10-20T12:06:00.000Z", KROMEDE); // a rift hour: Kromede and its pair Bakarma
const inAbyss = fight(nahma.id, "2026-10-20T14:06:00.000Z", KROMEDE); // the Abyss: the pair, always
const muninHome = fight(gartua.id, "2026-10-22T14:06:00.000Z", MUNIN); // a home fight of Munin

test("the servers a fight allows: the home server, and with the pair in the Abyss and in a rift hour", () => {
  assert.equal(partnerServerAt(KROMEDE, Date.parse("2026-10-20T14:06:00Z")), BAKARMA);
  assert.equal(partnerServerAt(KROMEDE, 500), null, "no pairing on record before the first notice");
  assert.deepEqual([...allowedServersOfEncounter(home)!.servers], [KROMEDE]);
  assert.deepEqual([...allowedServersOfEncounter(rift)!.servers].sort(), [BAKARMA, KROMEDE]);
  assert.deepEqual([...allowedServersOfEncounter(inAbyss)!.servers].sort(), [BAKARMA, KROMEDE]);
});

// Ann: on Kromede, in a home fight - fine. Bob: filed under Munin though only Kromede can be at the home fight (and the rift fight adds only its pair).
// Dee: Elyos in the rift fight on Bakarma - the pair, fine. Eli: filed under Munin, in a Kromede home fight and in a Munin home fight: the two exclude each other.
const ann = player("Ann", kromedeRow, "Asmodian");
const bob = player("Bob", muninRow, "Asmodian");
const dee = player("Dee", serverRowOf(BAKARMA), "Elyos");
const eli = player("Eli", muninRow, "Asmodian");
join(home, ann);
join(rift, ann);
join(home, bob);
join(rift, bob);
join(rift, dee);
join(inAbyss, dee);
join(home, eli);
join(muninHome, eli);

test("a character's fights say which servers he can be on", () => {
  assert.deepEqual([...plausibleServersOf(ann)!], [KROMEDE]);
  assert.deepEqual([...plausibleServersOf(bob)!], [KROMEDE], "Asmodian, so Kromede and not its Elyos pair");
  assert.deepEqual([...plausibleServersOf(dee)!], [BAKARMA]);
  assert.equal(plausibleServersOf(player("Nobody", kromedeRow, "Asmodian")), null, "no world boss fight: nothing is known");
});

test("the characters whose server breaks the rule are found", () => {
  const names = findCrowdViolators().map((v) => v.name).sort();
  assert.deepEqual(names, ["Bob", "Eli"]);
});

test("the clean-up files a character under the one server his fights allow, drops the NC data and asks NC again; others it leaves alone", () => {
  db.insert(playerNcMatch).values({ playerId: bob, status: "verified", ncServerId: MUNIN, ncRace: 2, ncCharacterId: "nc-bob", via: "name", checkedAt: 1, nextTryAt: 9e15 }).run();
  db.insert(playerNcCharacter).values({ playerId: bob, characterId: "nc-bob", ncServerId: MUNIN, status: "ok", fetchedAt: 1, nextTryAt: 1 }).run();

  const dry = cleanUpCrowdServers(false);
  assert.deepEqual(dry.fixed.map((f) => [f.name, f.from, f.to]), [["Bob", MUNIN, KROMEDE]]);
  assert.deepEqual(dry.undecided.map((u) => u.name), ["Eli"], "Eli: his fights exclude each other, nothing is decided for him");
  assert.equal(db.select().from(players).where(eq(players.id, bob)).get()!.serverId, muninRow, "a dry run writes nothing");

  const done = cleanUpCrowdServers(true, 5000);
  assert.equal(done.fixed.length, 1);
  assert.equal(db.select().from(players).where(eq(players.id, bob)).get()!.serverId, kromedeRow);
  assert.equal(db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, bob)).all().length, 0, "the NC data was a namesake's");
  const match = db.select().from(playerNcMatch).where(eq(playerNcMatch.playerId, bob)).get()!;
  assert.deepEqual([match.status, match.ncServerId, match.ncCharacterId, match.nextTryAt], ["client", KROMEDE, null, 5000], "asked at NC again at once");
  assert.equal(findCrowdViolators().map((v) => v.name).join(), "Eli");
});

test("a row of the same name that already sits on the right server is left alone", () => {
  const f = fight(gartua.id, "2026-10-21T14:06:00.000Z", KROMEDE);
  const twin = player("Twin", muninRow, "Asmodian");
  player("Twin", kromedeRow, "Asmodian");
  join(f, twin);
  const report = cleanUpCrowdServers(false);
  assert.deepEqual(report.duplicate.map((d) => d.name), ["Twin"]);
});
