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
