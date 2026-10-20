import assert from "node:assert/strict";
import test from "node:test";
import { isCharacterName, isCompletePlayer, withoutIncompletePlayers } from "./uploadSchema.js";

test("a character name is letters and digits of any script, no space or punctuation", () => {
  for (const ok of ["Aahz", "Namì", "NicoRobiñ", "Антонелла", "你在想Peach", "enjoy", "Enj0y", "Celîn"]) {
    assert.equal(isCharacterName(ok), true, ok);
  }
  for (const bad of ["Siliator of Deceit", "two words", "a-b", "x.y", "", " Aahz", "a".repeat(25)]) {
    assert.equal(isCharacterName(bad), false, bad);
  }
});

test("an upload's player with a spaced name is cut out like one without server or faction", () => {
  const good = { name: "Aahz", faction: "Elyos", serverId: 1304 };
  const spaced = { name: "Siliator of Deceit", faction: "Elyos", serverId: 1304 };
  assert.equal(isCompletePlayer(good), true);
  assert.equal(isCompletePlayer(spaced), false);
  const cut = withoutIncompletePlayers([good, spaced]);
  assert.deepEqual([cut.kept.length, cut.dropped.length], [1, 1]);
});
