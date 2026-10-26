import { test } from "node:test";
import assert from "node:assert/strict";
import { chainsOf, shapeTimeline } from "./encounterTimeline.js";

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

test("effects on the boss: a monster skill gets its client names, an effect the text tables have no name for is left out", () => {
  const o = START;
  const out = shapeTimeline({
    startedAt: START,
    durationSeconds: 60,
    bossHp: null,
    bossDebuffs: {
      o,
      list: [
        { skillId: 1600920, skill: "Whirlwind Shot", casts: 4, uptimePermille: 182, givers: ["Specimen"], spans: [10, 50], origin: o },
        { skillId: 1900000, skill: "Poison", casts: 2, uptimePermille: 1000, givers: ["Anna"], spans: [0, 600], origin: o },
        { skillId: 99999999, skill: "99999999", casts: 3, uptimePermille: 1000, givers: ["Anna"], spans: [0, 600], origin: o },
      ],
    },
    players: [],
  });
  assert.deepEqual(out.bossDebuffs.map((b) => b.skillId), [1900000, 1600920], "the unnamed number is not shown; the rest is sorted by uptime");
  assert.equal(out.bossDebuffs[1]!.names?.de, "Wirbelschuss");
  assert.equal(out.bossDebuffs[0]!.names?.de, "Vergiftung");
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

test("chain skills: a use of the first skill counts as taken when the follow-up comes within its window, a follow-up never used shows 0", () => {
  const skills = [{ id: 100, name: "Rending" }, { id: 200, name: "Smashing" }, { id: 300, name: "Keen" }];
  // Rending at 1 s (Smashing at 2 s = taken), at 10 s (Smashing at 14 s = too late), at 20 s (none); Keen used twice, its follow-up 400 is never used
  const uses: [number, number][] = [[1, 0], [2, 1], [10, 0], [14, 1], [20, 0], [30, 2], [40, 2]];
  const table = { "100": [[200, 3000]] as [number, number][], "300": [[400, 3000]] as [number, number][] };
  const out = chainsOf(skills, uses, table, (id) => (id === 400 ? { name: "Rupture", icon: null } : null));
  const rending = out.find((c) => c.to.id === 200)!;
  assert.deepEqual([rending.uses, rending.taken, rending.windowSeconds], [3, 1, 3]);
  const keen = out.find((c) => c.to.id === 400)!;
  assert.deepEqual([keen.uses, keen.taken, keen.to.name], [2, 0, "Rupture"]);
});

test("the focus of a player is given in seconds, nothing for a player without it", () => {
  const out = shapeTimeline({
    startedAt: START,
    durationSeconds: 60,
    bossHp: null,
    players: [
      { id: 1, name: "Anna", className: "Gladiator", timeline: { origin: START, skills: [{ id: 11, name: "Slam" }], times: [10], index: [0], deaths: [], focus: { onBoss: 453, onOthers: 120, switches: 7 } }, buffs: [] },
      { id: 2, name: "Bob", className: "Cleric", timeline: { origin: START, skills: [{ id: 21, name: "Heal" }], times: [0], index: [0], deaths: [] }, buffs: [] },
    ],
  });
  assert.deepEqual(out.players[0]!.focus, { onBoss: 45.3, onOthers: 12, switches: 7 });
  assert.equal(out.players[1]!.focus, null);
});
