import assert from "node:assert/strict";
import { test } from "node:test";
import { factionOfServerName, withServerFaction } from "./factions.js";

test("a server decides the faction", () => {
  assert.equal(factionOfServerName("Europe - Kaisinel"), "Elyos");
  assert.equal(factionOfServerName("Europe - Israphel"), "Asmodian");
  assert.equal(factionOfServerName("Europe - Nowhere"), "");
  assert.equal(factionOfServerName(null), "");
});

test("a row of a known server takes the server's faction, an unknown server keeps the stored one", () => {
  assert.equal(withServerFaction({ serverName: "Europe - Tiamat", faction: "" }).faction, "Elyos");
  assert.equal(withServerFaction({ serverName: "Europe - Tiamat", faction: "Asmodian" }).faction, "Elyos");
  assert.equal(withServerFaction({ serverName: "Somewhere", faction: "Asmodian" }).faction, "Asmodian");
  assert.equal(withServerFaction({ serverName: null }).faction, "");
});
