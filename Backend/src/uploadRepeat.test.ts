import assert from "node:assert/strict";
import test from "node:test";

const { MAX_COPIES, RepeatGuard, repeatKey } = await import("./uploadRepeat.js");

test("the first copies of a fight are taken, the rest is refused", () => {
  const guard = new RepeatGuard();
  const key = repeatKey("ip", 2600068, "2026-11-02T11:26:30Z", "2026-11-02T12:07:17Z");
  for (let i = 0; i < MAX_COPIES; i += 1) {
    assert.equal(guard.admit(key, 1000 + i), true);
  }
  assert.equal(guard.admit(key, 2000), false);
  assert.equal(guard.admit(key, 3000), false);
});

test("another fight, another uploader or another end time is a new fight", () => {
  const guard = new RepeatGuard(1);
  assert.equal(guard.admit(repeatKey("a", 1, "s", "e"), 0), true);
  assert.equal(guard.admit(repeatKey("a", 1, "s", "e"), 1), false);
  assert.equal(guard.admit(repeatKey("b", 1, "s", "e"), 2), true);
  assert.equal(guard.admit(repeatKey("a", 2, "s", "e"), 3), true);
  assert.equal(guard.admit(repeatKey("a", 1, "s", "e2"), 4), true);
});

test("the table stays bounded when it is flooded with distinct fights", () => {
  const guard = new RepeatGuard();
  for (let i = 0; i < 12_000; i += 1) {
    guard.admit(`k${i}`, i);
  }
  assert.ok(guard.size <= 5_000);
});
