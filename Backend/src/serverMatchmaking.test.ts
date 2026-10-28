import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { parseAbyssMatchmaking, parseMatchmaking, recordNotice, regionOfTitle, resetMatchmaking, matchmakingView } from "./serverMatchmaking.js";

const fixture = (name: string) => (JSON.parse(readFileSync(new URL(`./fixtures/nc-server-matchmaking-${name}.json`, import.meta.url), "utf8")) as { article: { content: { content: string } } }).article.content.content;
const html = fixture("asia");

test("asia notice: eight pairs, the update time spans the rows below it", () => {
  const pairs = parseMatchmaking(html);
  assert.deepEqual(pairs.map((p) => [p.elyos, p.asmodian]), [["Siel", "Israphel"], ["Nezekan", "Zikel"], ["Vaizel", "Triniel"], ["Kaisinel", "Lumiel"], ["Yustiel", "Marchutan"], ["Ariel", "Azphel"], ["Fregion", "Ereshkigal"], ["Meslamtaeda", "Beritra"]]);
  assert.equal(pairs[0].updated, "October 5, 2026, 10:00 PM (JST)");
  assert.equal(pairs[5].updated, "October 5, 2026, 10:00 PM (JST)");
  assert.equal(pairs[6].updated, "October 5, 2026, 10:33 PM (JST)");
  assert.equal(pairs[7].updated, "October 5, 2026, 11:10 PM (JST)");
});

test("region from the title, a notice without a table gives no pairs", () => {
  assert.equal(regionOfTitle("[Notice] Server Matchmaking Information (Asia)"), "Asia");
  assert.equal(regionOfTitle("Server Matchmaking Information"), null);
  assert.deepEqual(parseMatchmaking("<p>Matchmaking is coming.</p>"), []);
});

test("a changed pairing adds a history version, a new time alone does not", () => {
  resetMatchmaking();
  const item = { id: "a".repeat(24), title: "[Notice] Server Matchmaking Information (Asia)", postedAt: "2026-10-05T12:00:00.000Z" };
  const pairs = parseMatchmaking(html);
  recordNotice(item, pairs, 1);
  recordNotice(item, pairs.map((p) => ({ ...p, updated: "later" })), 2);
  recordNotice(item, [...pairs.slice(0, 7), { ...pairs[7], asmodian: "Nemon" }], 3);
  const view = matchmakingView();
  assert.equal(view.regions.length, 1);
  assert.equal(view.regions[0].pairs[7].asmodian, "Nemon");
});

test("launch pairings notice (Pair # rows): every region by its heading, Europe has 18 pairs", () => {
  const pairs = parseMatchmaking(fixture("launch-pairings"));
  const count = (r: string) => pairs.filter((p) => p.region === r).length;
  assert.deepEqual(["Europe", "NA West", "NA East", "LATAM", "Asia"].map(count), [18, 5, 8, 6, 5]);
  assert.deepEqual(pairs.filter((p) => p.region === "Europe").slice(3, 4).map((p) => [p.elyos, p.asmodian]), [["Kaisinel", "Lumiel"]]);
});

test("lineup notice: phases per block, Europe has the later pairs with their time, the rowspan time is not carried into an empty cell", () => {
  const eu = parseMatchmaking(fixture("lineup")).filter((p) => p.region === "Europe");
  assert.equal(eu.length, 23);
  assert.deepEqual(eu.slice(0, 1).map((p) => [p.elyos, p.asmodian, p.phase, p.updated]), [["Nania", "Hadala", "Launch", null]]);
  const ishtar = eu.find((p) => p.elyos === "Ishtar");
  assert.deepEqual([ishtar?.asmodian, ishtar?.phase, ishtar?.updated], ["Fafnir", "Launch", "09:30 PDT / 18:30 CEST"]);
  assert.equal(eu.find((p) => p.elyos === "Siel")?.phase, "Advanced Access");
});

test("abyss + rift notice (28 Oct): South America is LATAM, the abyss tables with factions are read apart from the rift pairs", () => {
  const notice = fixture("abyss-rift");
  const count = (list: { region: string | null }[], r: string) => list.filter((p) => p.region === r).length;
  const rift = parseMatchmaking(notice);
  assert.deepEqual(["Europe", "NA East", "NA West", "LATAM", "Asia"].map((r) => count(rift, r)), [23, 9, 5, 6, 10]);
  assert.deepEqual(rift.filter((p) => p.region === "LATAM").slice(5).map((p) => [p.elyos, p.asmodian]), [["Vaizel", "Azphel"]]);
  const abyss = parseAbyssMatchmaking(notice);
  assert.deepEqual(["Europe", "NA East", "NA West", "LATAM", "Asia"].map((r) => count(abyss, r)), [23, 9, 5, 6, 10]);
  assert.deepEqual(abyss[0], { region: "Europe", server1: "Triniel", server2: "Lumiel", faction1: "Asmodian", faction2: "Asmodian" });
  assert.deepEqual(abyss.find((p) => p.region === "Europe" && p.server1 === "Zikel"), { region: "Europe", server1: "Zikel", server2: "Kaisinel", faction1: "Asmodian", faction2: "Elyos" });
  assert.deepEqual(parseAbyssMatchmaking(fixture("lineup")), []);
});
