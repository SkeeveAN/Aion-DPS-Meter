import { test } from "node:test";
import assert from "node:assert/strict";
import { chainRowsOf } from "./chainUsage.js";

test("chain rows: per used skill with a follow-up the uses and the ones followed in time", () => {
  const timeline = {
    skills: [{ id: 100, name: "Rending" }, { id: 200, name: "Smashing" }],
    // Rending at 1 s and 10 s, Smashing at 2 s: the first use is followed, the second is not (times in tenths of a second)
    times: [10, 20, 100],
    index: [0, 1, 0],
  };
  assert.deepEqual(chainRowsOf(timeline, { "100": [[200, 3000]], "300": [[400, 3000]] }), [{ skillId: 100, nextId: 200, offered: 2, taken: 1 }]);
});
