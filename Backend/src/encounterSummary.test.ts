import assert from "node:assert/strict";
import test from "node:test";
import { shapeTimeline } from "./encounterTimeline.js";
import { summarizeEncounter } from "./encounterSummary.js";

const START = "2026-01-01T20:00:00.000Z";
const encounter = { id: 7, bossId: 3, durationSeconds: 20, groupIDps: 1234, isPartial: false };

test("the summary gives null, not 0, where no client sent the data", () => {
  const s = summarizeEncounter(encounter, [{ participantId: 1, className: "Gladiator", estimated: false, totalDamage: 500 }], null);
  assert.equal(s.bossDamage, 500);
  for (const key of ["addDamage", "deaths", "aliveShare", "bossFocusShare", "buffUsableShare", "damageCurve", "bossHp"] as const) {
    assert.equal(s[key], null, key);
  }
  assert.deepEqual(s.coverage, { players: 1, withTimeline: 0, withAnalysis: 0 });
});

test("the summary adds up deaths, alive share, focus, usable buff share and the cumulative curve; estimated rows and pets stay out", () => {
  const roster = [
    { participantId: 1, className: "Gladiator", estimated: false, totalDamage: 300 },
    { participantId: 2, className: "Chanter", estimated: false, totalDamage: 100 },
    { participantId: 3, className: "Ranger", estimated: true, totalDamage: 9999 },
    { participantId: 4, className: "?", estimated: false, totalDamage: 50 },
  ];
  const timeline = shapeTimeline({
    startedAt: START,
    durationSeconds: 20,
    bossHp: { o: START, p: [0, 1000, 200, 0] },
    players: [
      {
        id: 1,
        name: "Anna",
        className: "Gladiator",
        timeline: { origin: START, skills: [{ id: 11, name: "Slam" }], times: [10, 150], index: [0, 0], deaths: [100], focus: { onBoss: 120, onOthers: 40, switches: 2 }, analysis: { damage: [100, 200], addDamage: 70, dead: [100, 150], taken: [] } },
        buffs: [{ skillId: 5000, skill: "Mantra", casts: 1, uptimePermille: 1000, givers: ["Bob"], spans: [0, 200], origin: START }],
      },
      { id: 2, name: "Bob", className: "Chanter", timeline: { origin: START, skills: [{ id: 12, name: "Heal" }], times: [10], index: [0], deaths: [], focus: { onBoss: 40, onOthers: 0, switches: 0 }, analysis: { damage: [10, 10], addDamage: 0, dead: [], taken: [] } }, buffs: [] },
      { id: 3, name: "Cara", className: "Ranger", estimated: true, timeline: { origin: START, skills: [{ id: 13, name: "Shot" }], times: [10], index: [0], deaths: [200], focus: { onBoss: 1, onOthers: 999, switches: 0 }, analysis: { damage: [999], addDamage: 5, dead: [], taken: [] } }, buffs: [] },
    ],
  });
  const s = summarizeEncounter(encounter, roster, timeline);
  assert.equal(s.bossDamage, 400, "the estimated row and the pet are not added in");
  assert.equal(s.addDamage, 70);
  assert.equal(s.deaths, 1);
  assert.equal(s.aliveShare, 0.875, "Anna lived 75 % of the fight, Bob 100 %");
  assert.equal(s.bossFocusShare, 0.8, "160 of 200 tenths on the boss");
  assert.equal(s.damageCurve![0], 110);
  assert.equal(s.damageCurve![1], 320);
  assert.deepEqual(s.bossHp, [[0, 1000], [20, 0]]);
  assert.deepEqual(s.coverage, { players: 3, withTimeline: 2, withAnalysis: 2 });
  assert.ok(s.buffUsableShare !== null && s.buffUsableShare >= 0 && s.buffUsableShare <= 1);
});
