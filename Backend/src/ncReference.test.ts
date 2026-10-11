import { test } from "node:test";
import assert from "node:assert/strict";
import { compareClasses, compareServers } from "./ncReference.js";

const ours = (id: number) => ({ 1304: "Europe - Kaisinel", 2304: "Europe - Lumiel", 1330: "Talisra [1330]" }[id] ?? null);

test("servers: a live server of ours with another name, and a server we have no live entry for, are reported", () => {
  const list = [{ serverId: 1304, serverName: "Kaisinel" }, { serverId: 2304, serverName: "Lumielle" }, { serverId: 1330, serverName: "Talisra" }, { serverId: 1399, serverName: "Neu" }];
  assert.deepEqual(compareServers("eu", list, ours), [
    'eu: server 2304 is "Lumielle" at NC, "Lumiel" in our table',
    "eu: NC lists server 1330 (Talisra), our table has no live server for it",
    "eu: NC lists server 1399 (Neu), our table has no live server for it",
  ]);
  assert.deepEqual(compareServers("eu", [{ serverId: 1304, serverName: "Kaisinel" }], ours), []);
  assert.deepEqual(compareServers("eu", "x", ours), []);
});

test("classes: the formula (pcId - 1) / 4 and our names against NC's list", () => {
  assert.deepEqual(compareClasses([{ id: 5, classText: "Gladiator" }, { id: 8, classText: "Gladiator" }, { id: 21, classText: "Spiritmaster" }]), []);
  assert.deepEqual(compareClasses([{ id: 37, classText: "Aethertech" }, { id: 9, classText: "Ranger" }]), [
    'pcId 37 is "Aethertech" at NC, unknown in our table',
    'pcId 9 is "Ranger" at NC, "Templar" in our table',
  ]);
});
