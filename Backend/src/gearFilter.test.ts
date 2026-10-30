import { test } from "node:test";
import assert from "node:assert/strict";
import { gearEdges, parseGearRange } from "./gearFilter.js";

test("a gear score range is read from the query, anything odd is no filter", () => {
  assert.deepEqual(parseGearRange("1500-2000"), { min: 1500, max: 2000 });
  assert.deepEqual(parseGearRange("-1500"), { min: 0, max: 1500 });
  assert.deepEqual(parseGearRange("2000-"), { min: 2000, max: Number.MAX_SAFE_INTEGER });
  for (const bad of [undefined, "", "-", "abc", "2000-1500", "1-2-3", "1e3-5", "100000-"]) {
    assert.equal(parseGearRange(bad), null, String(bad));
  }
});

test("a combat power range has a leading p and may be longer than a gear score", () => {
  assert.deepEqual(parseGearRange("p100000-"), { min: 100000, max: Number.MAX_SAFE_INTEGER, metric: "power" });
  assert.deepEqual(parseGearRange("p-75000"), { min: 0, max: 75000, metric: "power" });
  assert.deepEqual(parseGearRange("p75000-120000"), { min: 75000, max: 120000, metric: "power" });
  for (const bad of ["p", "p-", "p200-100", "pp1-2", "p12345678-"]) {
    assert.equal(parseGearRange(bad), null, bad);
  }
});

test("an item level range has a leading i", () => {
  assert.deepEqual(parseGearRange("i2750-2850"), { min: 2750, max: 2850, metric: "level" });
  assert.deepEqual(parseGearRange("i-2750"), { min: 0, max: 2750, metric: "level" });
  assert.deepEqual(parseGearRange("i2850-"), { min: 2850, max: Number.MAX_SAFE_INTEGER, metric: "level" });
  for (const bad of ["i", "i-", "i3000-2000", "ii1-2", "i123456-"]) {
    assert.equal(parseGearRange(bad), null, bad);
  }
});

test("the edges of the filter follow the known values: thirds, rounded, none when there are too few", () => {
  const spread = Array.from({ length: 30 }, (_, i) => 2600 + i * 30); // 2600 .. 3470
  const edges = gearEdges(spread, "level");
  assert.equal(edges.length, 2);
  assert.ok(edges[0] > 2600 && edges[1] > edges[0] && edges[1] < 3470);
  assert.ok(edges.every((e) => e % 50 === 0));
  assert.deepEqual(gearEdges(spread.slice(0, 11), "level"), []);
  assert.deepEqual(gearEdges(Array(40).fill(2700), "level"), []);
  assert.ok(gearEdges(Array.from({ length: 30 }, (_, i) => 60000 + i * 3000), "power").every((e) => e % 1000 === 0));
});
