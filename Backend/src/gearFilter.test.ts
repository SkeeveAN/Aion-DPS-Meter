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
