import { test } from "node:test";
import assert from "node:assert/strict";
import { chainsOf, intersectSpans, rebinDamage, shapeTimeline, subtractSpans, unionSpans, usableOf } from "./encounterTimeline.js";

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

test("span arithmetic: union, intersection and subtraction of sorted spans", () => {
  assert.deepEqual(unionSpans([[5, 8], [0, 2], [1, 3], [8, 9], [4, 4]]), [[0, 3], [5, 9]]);
  assert.deepEqual(intersectSpans([[0, 10]], [[2, 4], [8, 12]]), [[2, 4], [8, 10]]);
  assert.deepEqual(subtractSpans([[0, 10]], [[2, 4], [8, 12]]), [[0, 2], [4, 8]]);
});

test("usable effect time: dead seconds, alive seconds without a skill use nearby, and the rest add up", () => {
  // the effect ran 0..20 s; the player was dead 10..14 s; he used skills at 1 s and 18 s (3 s to either side counts as busy)
  const out = usableOf([[0, 20]], [[10, 14]], [1, 18], 60);
  assert.deepEqual(out, { seconds: 20, dead: 4, idle: 7, active: 9 });
  assert.equal(out.dead + out.idle + out.active, out.seconds);
  // nothing outside the fight counts
  assert.equal(usableOf([[-5, 100]], [], [], 60).seconds, 60);
});

test("damage buckets are moved onto the encounter's clock without losing damage", () => {
  assert.deepEqual(rebinDamage([100, 200], 0, 10), [100, 200]);
  // an upload that began 2.5 s later: each bucket lands half in one, half in the next
  assert.deepEqual(rebinDamage([100, 200], 2.5, 10), [50, 150, 100]);
  assert.equal(rebinDamage([100, 200, 50], 1.7, 15).reduce((a, b) => a + b, 0), 350);
});

test("the analysis of the players becomes the group curve, alive share, lost buff time and coverage; estimated rows and players without one stay out", () => {
  const o = START;
  const out = shapeTimeline({
    startedAt: START,
    durationSeconds: 20,
    bossHp: null,
    players: [
      {
        id: 1,
        name: "Anna",
        className: "Gladiator",
        timeline: {
          origin: o,
          skills: [{ id: 11, name: "Slam" }],
          times: [10, 150],
          index: [0, 0],
          deaths: [100],
          analysis: { damage: [100, 200, 0, 50], addDamage: 70, dead: [100, 150], taken: [{ from: "boss", skill: "Smash", hits: 2, total: 900, max: 500 }] },
        },
        buffs: [{ skillId: 5000, skill: "Mantra", casts: 1, uptimePermille: 1000, givers: ["Bob"], spans: [0, 200], origin: o }],
      },
      { id: 2, name: "Bob", className: "Chanter", timeline: { origin: o, skills: [{ id: 12, name: "Heal" }], times: [10], index: [0], deaths: [], analysis: { damage: [10, 10, 10, 10], addDamage: 0, dead: [], taken: [] } }, buffs: [] },
      { id: 3, name: "Cara", className: "Ranger", estimated: true, timeline: { origin: o, skills: [{ id: 13, name: "Shot" }], times: [10], index: [0], deaths: [], analysis: { damage: [999], addDamage: 0, dead: [], taken: [] } }, buffs: [] },
      { id: 4, name: "Dan", className: "Templar", timeline: null, buffs: [] },
    ],
  });
  const a = out.analysis!;
  assert.equal(a.bucketSeconds, 5);
  assert.deepEqual(a.groupDamage, [110, 210, 10, 60]);
  assert.deepEqual(a.coverage, { players: 4, withAnalysis: 2 });
  assert.deepEqual(a.players.map((p) => p.name), ["Anna", "Bob"]);
  const anna = a.players[0]!;
  assert.equal(anna.deadSeconds, 5);
  assert.equal(anna.aliveShare, 0.75);
  assert.equal(anna.deaths, 1);
  assert.equal(anna.addDamage, 70);
  assert.deepEqual(anna.taken[0], { from: "boss", skill: "Smash", hits: 2, total: 900, max: 500 });
  // the effect ran the whole 20 s; she was dead 10..15 s and used skills at 1 s and 15 s: busy 0..4 and 12..18 (of which 12..15 she was dead... she revived at 15)
  const usable = out.players[0]!.buffs[0]!.usable!;
  assert.equal(usable.seconds, 20);
  assert.equal(usable.dead, 5);
  assert.equal(usable.dead + usable.idle + usable.active, 20);
});

test("a fight whose clients sent no analysis has none and no usable time, and nothing breaks", () => {
  const out = shapeTimeline({
    startedAt: START,
    durationSeconds: 20,
    bossHp: null,
    players: [{ id: 1, name: "Anna", className: "Gladiator", timeline: { origin: START, skills: [{ id: 11, name: "Slam" }], times: [10], index: [0], deaths: [] }, buffs: [{ skillId: 5000, skill: "Mantra", casts: 1, uptimePermille: 500, givers: ["Bob"], spans: [0, 100], origin: START }] }, { id: 2, name: "Bob", className: "Chanter", timeline: null, buffs: [] }],
  });
  assert.equal(out.analysis, undefined);
  assert.equal(out.players[0]!.buffs[0]!.usable, undefined);
});
