import assert from "node:assert/strict";
import test from "node:test";

const { retrying, isTransientGithubFailure, HttpFailure } = await import("./retry.js");

test("retrying: tries again after a failure worth another try, waits the given times, and throws the last failure", async () => {
  const waits: number[] = [];
  const sleep = async (ms: number) => { waits.push(ms); };
  let calls = 0;
  const value = await retrying(async (attempt) => {
    calls++;
    if (attempt < 3) throw new HttpFailure(502, "bad gateway");
    return "done";
  }, isTransientGithubFailure, [10, 20], sleep);
  assert.equal(value, "done");
  assert.equal(calls, 3);
  assert.deepEqual(waits, [10, 20]);

  calls = 0;
  await assert.rejects(retrying(async () => { calls++; throw new HttpFailure(503, "down"); }, isTransientGithubFailure, [10, 20], sleep), /down/);
  assert.equal(calls, 3, "tries used up");

  calls = 0;
  await assert.rejects(retrying(async () => { calls++; throw new HttpFailure(401, "bad credentials"); }, isTransientGithubFailure, [10, 20], sleep), /bad credentials/);
  assert.equal(calls, 1, "a wrong token is not tried again");
});

test("which GitHub failures are worth another try", () => {
  assert.equal(isTransientGithubFailure(new TypeError("fetch failed")), true, "no connection");
  assert.equal(isTransientGithubFailure(Object.assign(new Error("t"), { name: "TimeoutError" })), true);
  assert.equal(isTransientGithubFailure(new HttpFailure(500, "x")), true);
  assert.equal(isTransientGithubFailure(new HttpFailure(429, "x")), true);
  assert.equal(isTransientGithubFailure(new HttpFailure(400, "We received a malformed request from your client")), true);
  assert.equal(isTransientGithubFailure(new HttpFailure(400, "Problems parsing JSON")), false);
  assert.equal(isTransientGithubFailure(new HttpFailure(404, "Not Found")), false);
  assert.equal(isTransientGithubFailure(new HttpFailure(422, "sha wasn't supplied")), false);
});
