import { test } from "node:test";
import assert from "node:assert/strict";
import { boxesByClass, quantile, thinOut } from "./classStats.js";

test("quantile interpolates between neighbours", () => {
  assert.equal(quantile([], 0.5), 0);
  assert.equal(quantile([7], 0.9), 7);
  assert.equal(quantile([1, 2, 3, 4], 0.5), 2.5);
  assert.equal(quantile([1, 2, 3, 4, 5], 0.25), 2);
});

test("boxesByClass: per class, no empty rows, strongest median first", () => {
  const boxes = boxesByClass([
    { className: "Cleric", idps: 100 },
    { className: "Cleric", idps: 300 },
    { className: "Cleric", idps: 200 },
    { className: "Ranger", idps: 900 },
    { className: "Ranger", idps: 0 },
    { className: "Ranger", idps: 700 },
  ]);
  assert.deepEqual(boxes.map((b) => b.className), ["Ranger", "Cleric"]);
  assert.deepEqual(boxes[1], { className: "Cleric", samples: 3, min: 100, q1: 150, median: 200, q3: 250, max: 300, values: [100, 200, 300] });
  assert.equal(boxes[0]!.samples, 2);
});

test("thinOut keeps short lists and spreads long ones with both ends", () => {
  assert.deepEqual(thinOut([1, 2, 3], 5), [1, 2, 3]);
  const thin = thinOut(Array.from({ length: 1000 }, (_, i) => i), 10);
  assert.equal(thin.length, 10);
  assert.equal(thin[0], 0);
  assert.equal(thin[9], 999);
});

test("skillStatsByClass: shares of the class damage, rare skills and thin classes left out", async () => {
  const { skillStatsByClass } = await import("./classStats.js");
  const rows = [
    { className: "Gladiator", skill: "Big", runs: 20, hits: 100, critHits: 50, damage: 3000 },
    { className: "Gladiator", skill: "Small", runs: 5, hits: 20, critHits: 2, damage: 1000 },
    { className: "Gladiator", skill: "Rare", runs: 1, hits: 5, critHits: 5, damage: 900 },
    { className: "Cleric", skill: "Only", runs: 2, hits: 4, critHits: 0, damage: 100 },
  ];
  const result = skillStatsByClass(rows, new Map([["Gladiator", 20], ["Cleric", 2]]));
  assert.equal(result.length, 1, "a class with two rows has no table");
  const [glad] = result;
  assert.equal(glad!.samples, 20);
  assert.deepEqual(glad!.skills.map((s) => s.skill), ["Big", "Small"], "the skill used by one row of twenty is left out");
  assert.equal(glad!.skills[0]!.runShare, 1);
  assert.equal(glad!.skills[0]!.hitsPerRun, 5);
  assert.equal(glad!.skills[0]!.critRate, 0.5);
  assert.equal(glad!.skills[0]!.averageHit, 30);
  assert.ok(Math.abs(glad!.skills[0]!.damageShare - 3000 / 4900) < 1e-9);
});

test("skillStatsByClass: chain rate and average level appear with enough samples only", async () => {
  const { skillStatsByClass } = await import("./classStats.js");
  const rows = [
    { className: "Gladiator", skill: "Rending Blow", runs: 20, hits: 100, critHits: 50, damage: 3000 },
    { className: "Gladiator", skill: "Overhead Slam", runs: 20, hits: 100, critHits: 50, damage: 2000 },
  ];
  const extras = new Map([
    ["Gladiator\u0000Rending Blow", { chainOffered: 40, chainTaken: 30, levelSum: 150, levelCount: 10 }],
    ["Gladiator\u0000Overhead Slam", { chainOffered: 4, chainTaken: 4, levelSum: 30, levelCount: 3 }],
  ]);
  const [glad] = skillStatsByClass(rows, new Map([["Gladiator", 20]]), extras);
  assert.deepEqual([glad!.skills[0]!.chainRate, glad!.skills[0]!.avgLevel], [0.75, 15]);
  assert.deepEqual([glad!.skills[1]!.chainRate, glad!.skills[1]!.avgLevel], [null, null]);
});
