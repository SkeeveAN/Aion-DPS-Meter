import { test } from "node:test";
import assert from "node:assert/strict";
import { parseGearRange } from "./gearFilter.js";

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
