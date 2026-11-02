import assert from "node:assert/strict";
import test from "node:test";

const { yieldToEventLoop } = await import("./eventLoop.js");

test("a timer that is already due runs while a chain of promises yields to the loop", async () => {
  let fired = false;
  setTimeout(() => {
    fired = true;
  }, 0);
  const until = Date.now() + 5;
  while (Date.now() < until) {
    // busy for a moment so the timer is due
  }
  await Promise.resolve();
  assert.equal(fired, false, "promise turns alone do not let timers run");
  await yieldToEventLoop();
  await yieldToEventLoop();
  assert.equal(fired, true);
});
