import { test } from "node:test";
import assert from "node:assert/strict";
import { shapeTimeline } from "./encounterTimeline.js";

const START = "2026-01-01T20:00:00.000Z";

test("the time axis is lined up on the start of the encounter and the effects at each use are counted", () => {
  const out = shapeTimeline({
    startedAt: START,
    durationSeconds: 60,
    // this upload began 2 s after the encounter's first one: everything it knows is 2 s later on the encounter's clock
    bossHp: { o: "2026-01-01T20:00:02.000Z", p: [0, 1000, 100, 900] },
    players: [
      {
        id: 1,
        name: "Anna",
        className: "Gladiator",
        timeline: { origin: "2026-01-01T20:00:02.000Z", skills: [{ id: 11, name: "Slam" }, { id: 12, name: "Cut" }], times: [10, 50, 90, 130], index: [0, 1, 0, 0], deaths: [200] },
        buffs: [
          // a group effect (given by Bob) on Anna from 0.5 s to 5.5 s after her upload began
          { skillId: 5000, skill: "Mantra", casts: 1, uptimePermille: 100, givers: ["Bob"], spans: [5, 55], origin: "2026-01-01T20:00:02.000Z" },
          { skillId: 6000, skill: "Own Boost", casts: 1, uptimePermille: 10, givers: [], spans: [], origin: "2026-01-01T20:00:02.000Z" },
        ],
      },
      { id: 2, name: "Bob", className: "Chanter", timeline: null, buffs: [] },
    ],
  });
  assert.deepEqual(out.bossHp, [[2, 1000], [12, 900]]);
  assert.equal(out.players.length, 1, "a player without a time axis is left out");
  const anna = out.players[0]!;
  assert.deepEqual(anna.uses.map((u) => u[0]), [3, 7, 11, 15]);
  assert.deepEqual(anna.deaths, [22]);
  assert.equal(anna.skills[0]!.uses, 3);
  assert.deepEqual(anna.buffs.map((b) => b.name), ["Mantra"], "an effect without spans is left out");
  assert.deepEqual(anna.buffs[0]!.spans, [[2.5, 7.5]]);
});

test("a group effect lies over the uses it covers", () => {
  const out = shapeTimeline({
    startedAt: START,
    durationSeconds: 60,
    bossHp: null,
    players: [
      {
        id: 1,
        name: "Anna",
        className: "Gladiator",
        timeline: { origin: START, skills: [{ id: 11, name: "Slam" }], times: [10, 30, 50, 100], index: [0, 0, 0, 0], deaths: [] },
        buffs: [{ skillId: 5000, skill: "Mantra", casts: 1, uptimePermille: 100, givers: ["Bob"], spans: [0, 60], origin: START }],
      },
      { id: 2, name: "Bob", className: "Chanter", timeline: { origin: START, skills: [{ id: 21, name: "Mantra" }], times: [0], index: [0], deaths: [] }, buffs: [] },
    ],
  });
  const anna = out.players.find((p) => p.name === "Anna")!;
  assert.equal(anna.buffs.length, 1);
  assert.equal(anna.buffs[0]!.kind, "group");
  assert.deepEqual(anna.buffs[0]!.spans, [[0, 6]]);
  // uses at 1 s, 3 s and 5 s are inside 0..6 s, the one at 10 s is not
  assert.deepEqual(anna.skillBuffs, [{ skill: 0, uses: 4, buffs: [{ skillId: 5000, share: 0.75 }] }]);
});
