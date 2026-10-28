import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-matchmaking-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { parseAbyssMatchmaking, parseMatchmaking } = await import("./serverMatchmaking.js");
const { servers } = await import("./db/schema.js");
const { currentMatches, matchedServer, syncAbyssToDb, syncMatchmakingToDb } = await import("./serverMatchmakingDb.js");

const fixture = (name: string) => (JSON.parse(readFileSync(new URL(`./fixtures/nc-server-matchmaking-${name}.json`, import.meta.url), "utf8")) as { article: { content: { content: string } } }).article.content.content;
const europe = () => ({ region: "Europe", noticeId: "n1", pairs: parseMatchmaking(fixture("lineup")).filter((p) => p.region === "Europe") });

const upload = (name: string) => Number(db.insert(servers).values({ fingerprint: `aion2:${name.toLowerCase().replace(/\W+/g, "-")}`, displayName: name }).run().lastInsertRowid);
const kaisinelId = upload("Europe - Kaisinel");
const lumielId = upload("Europe - Lumiel");

test("first sync opens one row per pair and links the servers known from uploads", () => {
  assert.deepEqual(syncMatchmakingToDb([europe()], 1000), { opened: 23, closed: 0 });
  assert.equal(currentMatches("Europe").length, 23);
  assert.equal(matchedServer("Europe", "Kaisinel"), "Lumiel");
  assert.equal(matchedServer("Europe", "Lumiel"), "Kaisinel");
  assert.equal(matchedServer("Asia", "Kaisinel"), null);
  const kaisinel = currentMatches("Europe").find((m) => m.elyosServer === "Kaisinel");
  assert.deepEqual([kaisinel?.elyosServerId, kaisinel?.asmodianServerId], [kaisinelId, lumielId]);
  assert.equal(currentMatches("Europe").find((m) => m.elyosServer === "Ishtar")?.elyosServerId, null);
});

test("the same list again changes nothing; a changed pairing closes the old row and opens a new one", () => {
  assert.deepEqual(syncMatchmakingToDb([europe()], 2000), { opened: 0, closed: 0 });
  const changed = europe();
  changed.pairs = changed.pairs.map((p) => (p.elyos === "Kaisinel" ? { ...p, asmodian: "Beritra" } : p));
  assert.deepEqual(syncMatchmakingToDb([changed], 3000), { opened: 1, closed: 1 });
  assert.equal(matchedServer("Europe", "Kaisinel"), "Beritra");
  assert.equal(matchedServer("Europe", "Lumiel"), null);
  assert.equal(currentMatches("Europe").length, 23);
});

test("a server that uploads later gets its id on the open row", () => {
  const ishtarId = upload("Europe - Ishtar");
  const list = europe();
  list.pairs = list.pairs.map((p) => (p.elyos === "Kaisinel" ? { ...p, asmodian: "Beritra" } : p)); // the state the test before left
  assert.deepEqual(syncMatchmakingToDb([list], 3500), { opened: 0, closed: 0 });
  assert.equal(currentMatches("Europe").find((m) => m.elyosServer === "Ishtar")?.elyosServerId, ishtarId);
});

test("a region the notices do not mention keeps its rows", () => {
  assert.deepEqual(syncMatchmakingToDb([{ region: "Asia", noticeId: "n2", pairs: [] }], 4000), { opened: 0, closed: 0 });
  assert.equal(currentMatches("Europe").length, 23);
});

test("abyss pairings: same sync scheme, a changed pairing closes the old row", () => {
  const abyss = parseAbyssMatchmaking(fixture("abyss-rift")).filter((p) => p.region === "Europe");
  assert.deepEqual(syncAbyssToDb([{ region: "Europe", noticeId: "n2", pairs: abyss }], 5000), { opened: 23, closed: 0 });
  assert.deepEqual(syncAbyssToDb([{ region: "Europe", noticeId: "n2", pairs: abyss }], 6000), { opened: 0, closed: 0 });
  assert.deepEqual(syncAbyssToDb([{ region: "Europe", noticeId: "n2", pairs: abyss.map((p) => (p.server1 === "Zikel" ? { ...p, server2: "Siel" } : p)) }], 7000), { opened: 1, closed: 1 });
});
