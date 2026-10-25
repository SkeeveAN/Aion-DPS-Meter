import assert from "node:assert/strict";
import test from "node:test";

const { NcPacing, DEFAULT_PACING } = await import("./ncPacing.js");

const pacing = () => new NcPacing({ ...DEFAULT_PACING, okPerStep: 10 }, () => {});

test("pacing: the gap starts slow and gets shorter in small steps while the answers are fine, down to the floor", () => {
  const p = pacing();
  assert.equal(p.gapMs(), 2000);
  for (let i = 0; i < 10; i++) p.note(200);
  assert.equal(p.gapMs(), 1800, "one step of 10 % after ten good answers");
  for (let i = 0; i < 10_000; i++) p.note(200);
  assert.equal(p.gapMs(), DEFAULT_PACING.floorGapMs);
});

test("pacing: a single server error changes nothing, a cluster of them raises the gap once, and it comes down again", () => {
  const p = pacing();
  for (let i = 0; i < 100; i++) p.note(200, 1000);
  const before = p.gapMs();
  p.note(502, 100_000);
  assert.equal(p.gapMs(), before, "one 502 is normal");
  p.note(502, 110_000);
  p.note(502, 120_000);
  assert.equal(p.gapMs(), Math.round(before * 1.5), "three in a minute: raised");
  p.note(502, 121_000);
  p.note(502, 122_000);
  assert.equal(p.gapMs(), Math.round(before * 1.5), "the same cluster does not raise it again");
  const raised = p.gapMs();
  for (let i = 0; i < 10; i++) p.note(200, 130_000);
  assert.ok(p.gapMs() < raised, "and it gets shorter again");
  p.note(502, 400_000);
  p.note(502, 410_000);
  p.note(502, 420_000);
  assert.ok(p.gapMs() > Math.round(raised * 0.9) - 1, "a later cluster raises it again");
});

test("pacing: a 429 or 403 stops all requests for a minute and doubles the gap; the gap never passes the ceiling", () => {
  const p = pacing();
  p.note(429, 5000);
  assert.equal(p.gapMs(), 4000);
  assert.equal(p.pausedUntil(), 5000 + DEFAULT_PACING.limitPauseMs);
  for (let i = 0; i < 20; i++) p.note(403, 5000 + i);
  assert.equal(p.gapMs(), DEFAULT_PACING.ceilingGapMs);
});
