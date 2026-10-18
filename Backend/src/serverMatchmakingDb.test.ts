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

const { parseMatchmaking } = await import("./serverMatchmaking.js");
const { currentMatches, matchedServer, syncMatchmakingToDb } = await import("./serverMatchmakingDb.js");

const fixture = (name: string) => (JSON.parse(readFileSync(new URL(`./fixtures/nc-server-matchmaking-${name}.json`, import.meta.url), "utf8")) as { article: { content: { content: string } } }).article.content.content;
const europe = () => ({ region: "Europe", noticeId: "n1", pairs: parseMatchmaking(fixture("lineup")).filter((p) => p.region === "Europe") });

test("first sync opens one row per pair and links the catalog servers that exist", () => {
  assert.deepEqual(syncMatchmakingToDb([europe()], 1000), { opened: 23, closed: 0 });
  assert.equal(currentMatches("Europe").length, 23);
  assert.equal(matchedServer("Europe", "Kaisinel"), "Lumiel");
  assert.equal(matchedServer("Europe", "Lumiel"), "Kaisinel");
  assert.equal(matchedServer("Asia", "Kaisinel"), null);
  const kaisinel = currentMatches("Europe").find((m) => m.elyosServer === "Kaisinel");
  assert.ok(kaisinel?.elyosCatalogId && kaisinel.asmodianCatalogId, "Kaisinel and Lumiel are in server_catalog");
  assert.equal(currentMatches("Europe").find((m) => m.elyosServer === "Ishtar")?.elyosCatalogId, null);
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

test("a region the notices do not mention keeps its rows", () => {
  assert.deepEqual(syncMatchmakingToDb([{ region: "Asia", noticeId: "n2", pairs: [] }], 4000), { opened: 0, closed: 0 });
  assert.equal(currentMatches("Europe").length, 23);
});
