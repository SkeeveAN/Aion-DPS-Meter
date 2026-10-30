import assert from "node:assert/strict";
import test from "node:test";
import { isCharacterName, isCompletePlayer, participantSchema, withoutIncompletePlayers } from "./uploadSchema.js";

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
  assert.equal(isCompletePlayer({ ...good, serverId: 2823 }), false, "a server id that names no server is no server");
  const cut = withoutIncompletePlayers([good, spaced]);
  assert.deepEqual([cut.kept.length, cut.dropped.length], [1, 1]);
});

test("the analysis of a time axis is optional, bounded and keeps its shape", () => {
  const base = { name: "Anna", className: "Gladiator", isSelf: true, totalDamage: 1, dps: 1, idps: 1, totalHealing: 0, hps: 0, skills: [], healSkills: [] };
  const timeline = { skills: [{ id: 1, name: "Slam" }], times: [1], index: [0] };
  assert.equal(participantSchema.safeParse({ ...base, timeline }).success, true, "an older client sends none");
  const ok = participantSchema.safeParse({ ...base, timeline: { ...timeline, analysis: { damage: [1, 2], addDamage: 3, dead: [10, 20], taken: [{ from: "add", skill: "Bite", hits: 1, total: 5, max: 5 }] } } });
  assert.equal(ok.success, true);
  assert.equal(ok.success && ok.data.timeline?.analysis?.taken[0]?.from, "add");
  const bad = (analysis: object) => participantSchema.safeParse({ ...base, timeline: { ...timeline, analysis } }).success;
  assert.equal(bad({ dead: [10] }), false, "dead spans pair up");
  assert.equal(bad({ damage: new Array(721).fill(0) }), false, "at most an hour of buckets");
  assert.equal(bad({ taken: [{ from: "boss-ish", skill: "x", hits: 1, total: 1, max: 1 }] }), false, "the source is one of three");
  assert.equal(bad({ taken: new Array(17).fill({ from: "boss", skill: "x", hits: 1, total: 1, max: 1 }) }), false, "at most 16 rows");
});
