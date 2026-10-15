import { test } from "node:test";
import assert from "node:assert/strict";
import { boxesByClass, quantile } from "./classStats.js";

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
  assert.deepEqual(boxes[1], { className: "Cleric", samples: 3, min: 100, q1: 150, median: 200, q3: 250, max: 300 });
  assert.equal(boxes[0]!.samples, 2);
});
