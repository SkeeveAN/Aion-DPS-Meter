import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-ncmatch-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { decide, parseHits, processMatch, noteClientServer, dueOf, MatchQueue, shardOf, MATCH_RETRY_AFTER } = await import("./ncMatch.js");
const { players, servers, playerProfiles } = await import("./db/schema.js");
const { linkPlayerGuild } = await import("./guilds.js");
const { eq } = await import("drizzle-orm");

type Hit = ReturnType<typeof parseHits>[number];
const hit = (o: Partial<Hit> & { name: string }): Hit => ({ race: 1, serverId: 1304, level: 45, classId: 1, characterId: null, ...o });
const subject = (o: Partial<Parameters<typeof decide>[1]> = {}) => ({ name: "Aahz", faction: "", className: "", classFromFight: false, level: null, legionServers: [], ...o });

test("a name with one character is that player", () => {
  const d = decide([hit({ name: "Aahz" }), hit({ name: "Aahza", serverId: 1305 })], subject());
  assert.equal(d.status, "verified");
  assert.equal(d.hit?.serverId, 1304);
  assert.equal(d.via, "name");
});

test("the same name on both factions is told apart by the faction", () => {
  const hits = [hit({ name: "Aahz", race: 2, serverId: 2303 }), hit({ name: "Aahz", race: 1, serverId: 1304 })];
  assert.equal(decide(hits, subject()).status, "ambiguous");
  assert.equal(decide(hits, subject({ faction: "Elyos" })).hit?.serverId, 1304);
  assert.equal(decide(hits, subject({ faction: "Asmodian" })).hit?.serverId, 2303);
});

test("class and level narrow the candidates, a legion decides the rest", () => {
  const hits = [hit({ name: "Miko", serverId: 1304, classId: 8, level: 45 }), hit({ name: "Miko", serverId: 1305, classId: 8, level: 45 }), hit({ name: "Miko", serverId: 1306, classId: 2, level: 45 })];
  assert.equal(decide(hits, subject({ name: "Miko", className: "Chanter" })).status, "ambiguous", "two Chanters remain");
  assert.equal(decide(hits, subject({ name: "Miko", className: "Chanter", legionServers: [1305] })).hit?.serverId, 1305);
  assert.equal(decide(hits, subject({ name: "Miko", className: "Chanter", legionServers: [1305] })).via, "legion");
  const lowLevels = [hit({ name: "Kai", serverId: 1304, level: 12 }), hit({ name: "Kai", serverId: 1305, level: 45 })];
  assert.equal(decide(lowLevels, subject({ name: "Kai", level: 40 })).hit?.serverId, 1305, "a character never loses levels");
});

test("nothing is guessed: no hit, a hit of the other faction, or a lone hit of another class give no match", () => {
  assert.equal(decide([], subject()).status, "none");
  assert.equal(decide([hit({ name: "Aahz", race: 2 })], subject({ faction: "Elyos" })).status, "none");
  assert.equal(decide([hit({ name: "Aahz", classId: 8 })], subject({ className: "Gladiator", classFromFight: true })).status, "none");
  assert.equal(decide([hit({ name: "Aahz", classId: 8 })], subject({ className: "Gladiator", classFromFight: false })).status, "verified", "a class from his profile only narrows, it does not veto");
});

test("a search answer is read defensively", () => {
  assert.deepEqual(parseHits("x"), []);
  const parsed = parseHits([{ name: "<strong>Aa</strong>hz", race: 1, serverId: 1304, level: 45, pcId: 6, characterId: "abc" }, { name: "Bad", race: 3, serverId: 1304 }, { name: "Bad2", race: 1, serverId: 7 }, null]);
  assert.equal(parsed.length, 1);
  assert.equal(parsed[0].name, "Aahz");
  assert.equal(parsed[0].classId, 1, "(6 - 1) / 4 = class 1, Gladiator");
});

test("regions", () => {
  assert.equal(shardOf("Europe - Kaisinel"), "eu");
  assert.equal(shardOf("Asia - Fregion"), "as");
  assert.equal(shardOf("NA West - Siel"), "naw");
  assert.equal(shardOf("Hogalum [1317]"), null);
});

// --- processMatch against a simulated NC search ---
const server = (fingerprint: string, displayName: string) => Number(db.insert(servers).values({ fingerprint, displayName }).run().lastInsertRowid);
const kaisinel = server("aion2:europe-kaisinel", "Europe - Kaisinel");
const fregion = server("aion2:asia-fregion", "Asia - Fregion");
let n = 0;
const player = (name: string, serverId: number, extra: Partial<typeof players.$inferInsert> = {}) =>
  Number(db.insert(players).values({ name, nameNormalized: name.toLowerCase(), serverId, slug: `${name.toLowerCase()}-${++n}`, ...extra }).run().lastInsertRowid);
const serverOf = (id: number) => (sqlite.prepare("select s.display_name d, p.faction f from players p join servers s on s.id = p.server_id where p.id = ?").get(id) as { d: string; f: string });
const matchRow = (id: number) => sqlite.prepare("select status, nc_server_id s, via from player_nc_match where player_id = ?").get(id) as { status: string; s: number | null; via: string | null } | undefined;

function ncAnswer(list: object[], seen: string[] = []) {
  return (async (input: unknown) => {
    seen.push(String(input));
    return new Response(JSON.stringify({ list, pagination: { page: 1, size: 40, total: list.length, endPage: 1 } }), { status: 200, headers: { "content-type": "application/json" } });
  }) as unknown as typeof fetch;
}
const raw = (o: { name: string; race?: number; serverId: number; level?: number; pcId?: number }) => ({ race: 1, level: 45, pcId: 6, ...o });

test("a single matching character moves the player to his real server and gives him his faction", async () => {
  const id = player("Wanderer", kaisinel);
  const seen: string[] = [];
  const status = await processMatch(id, ncAnswer([raw({ name: "<strong>Wanderer</strong>", race: 2, serverId: 2308 })], seen), 1_000_000);
  assert.equal(status, "verified");
  assert.deepEqual(serverOf(id), { d: "Europe - Beritra", f: "Asmodian" });
  assert.equal(matchRow(id)?.status, "verified");
  assert.match(seen[0], /region=eu/);
});

test("the region of the player's own server is searched", async () => {
  const id = player("Fernost", fregion);
  const seen: string[] = [];
  await processMatch(id, ncAnswer([raw({ name: "Fernost", serverId: 1507 })], seen), 1_000_000);
  assert.match(seen[0], /region=as/);
  assert.equal(serverOf(id).d, "Asia - Fregion");
});

test("a match that would put him on a server where his name is already taken changes nothing", async () => {
  const beritra = Number((sqlite.prepare("select id from servers where display_name = 'Europe - Beritra'").get() as { id: number }).id);
  player("Doppelt", beritra);
  const id = player("Doppelt", kaisinel);
  const status = await processMatch(id, ncAnswer([raw({ name: "Doppelt", serverId: 2308 })]), 1_000_000);
  assert.equal(status, "conflict");
  assert.equal(serverOf(id).d, "Europe - Kaisinel");
});

test("several fitting characters stay unclear and are asked again later, not now", async () => {
  const id = player("Haeufig", kaisinel);
  const status = await processMatch(id, ncAnswer([raw({ name: "Haeufig", serverId: 1304 }), raw({ name: "Haeufig", serverId: 1305 })]), 1_000_000);
  assert.equal(status, "ambiguous");
  assert.equal(serverOf(id).d, "Europe - Kaisinel");
  assert.deepEqual(dueOf([id], 1_000_000 + MATCH_RETRY_AFTER.ambiguous - 1), []);
  assert.deepEqual(dueOf([id], 1_000_000 + MATCH_RETRY_AFTER.ambiguous + 1), [id]);
});

test("a network problem is recorded and retried in an hour; nothing is changed", async () => {
  const id = player("Offline", kaisinel);
  const status = await processMatch(id, (async () => { throw new Error("down"); }) as unknown as typeof fetch, 1_000_000);
  assert.equal(status, "error");
  assert.equal(serverOf(id).d, "Europe - Kaisinel");
  assert.deepEqual(dueOf([id], 1_000_000 + MATCH_RETRY_AFTER.error + 1), [id]);
});

test("the verified members of a legion settle a name that has several characters", async () => {
  const mates = [player("Mate1", kaisinel, { guild: "Eisenfaust" }), player("Mate2", kaisinel, { guild: "Eisenfaust" })];
  const member = player("Gleichname", kaisinel, { guild: "Eisenfaust" });
  for (const id of [...mates, member]) linkPlayerGuild(id);
  for (const id of mates) noteClientServer(id, 1305, 1_000_000);
  const status = await processMatch(member, ncAnswer([raw({ name: "Gleichname", serverId: 1304 }), raw({ name: "Gleichname", serverId: 1305 })]), 1_000_000);
  assert.equal(status, "verified");
  assert.equal(matchRow(member)?.via, "legion");
  assert.equal(serverOf(member).d, "Europe - Yustiel", "1305 is Yustiel");
});

test("a player whose own client named his server is never asked", () => {
  const id = player("Selbst", kaisinel);
  noteClientServer(id, 1304, 1_000_000);
  assert.deepEqual(dueOf([id], 1_000_000 + 30 * 24 * 3_600_000), []);
});

test("a profile's class helps to pick, and a level lower than his own is no match", async () => {
  const id = player("Stufig", kaisinel);
  db.insert(playerProfiles).values({ playerId: id, source: "seen", classId: 8, level: 40 }).run();
  const status = await processMatch(id, ncAnswer([raw({ name: "Stufig", serverId: 1304, pcId: 34, level: 12 }), raw({ name: "Stufig", serverId: 1305, pcId: 34, level: 45 })]), 1_000_000);
  assert.equal(status, "verified");
  assert.equal(serverOf(id).d, "Europe - Yustiel");
});

test("the queue works one player at a time and takes each only once", async () => {
  const order: number[] = [];
  let active = 0;
  let overlap = false;
  const q = new MatchQueue(async (id) => {
    active++;
    overlap ||= active > 1;
    await new Promise((r) => setTimeout(r, 2));
    order.push(id);
    active--;
  });
  assert.equal(q.enqueue([1, 2, 2, 3]), 3);
  await q.idle();
  assert.deepEqual(order, [1, 2, 3]);
  assert.equal(overlap, false);
});

test("a player without a legion or region we can search is not due", () => {
  const unknown = server("aion2:aion-2-server-9999", "Hogalum [9999]");
  const id = player("Fremd", unknown);
  assert.deepEqual(dueOf([id]), []);
  assert.deepEqual(db.select().from(players).where(eq(players.id, id)).all().length, 1);
});
