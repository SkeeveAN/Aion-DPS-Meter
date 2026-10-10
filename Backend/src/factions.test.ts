import assert from "node:assert/strict";
import { test } from "node:test";
import { factionOfServerName, withServerFaction } from "./factions.js";

test("a server decides the faction", () => {
  assert.equal(factionOfServerName("Europe - Kaisinel"), "Elyos");
  assert.equal(factionOfServerName("Europe - Israphel"), "Asmodian");
  assert.equal(factionOfServerName("Europe - Nowhere"), "");
  assert.equal(factionOfServerName(null), "");
});

test("a row keeps the faction the client sent, only a row without one takes the server's", () => {
  assert.equal(withServerFaction({ serverName: "Europe - Tiamat", faction: "" }).faction, "Elyos");
  assert.equal(withServerFaction({ serverName: "Europe - Kaisinel", faction: "Asmodian" }).faction, "Asmodian");
  assert.equal(withServerFaction({ serverName: "Somewhere", faction: "Asmodian" }).faction, "Asmodian");
  assert.equal(withServerFaction({ serverName: null }).faction, "");
});
