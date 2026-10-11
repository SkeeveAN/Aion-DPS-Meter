import { test } from "node:test";
import assert from "node:assert/strict";
import { baseInterval, characterInterval, parseSeen, portraitInterval } from "./refreshPolicy.js";

const HOUR = 3_600_000;
const DAY = 24 * HOUR;
const NOW = 1_800_000_000_000;

test("the base wait follows the player's activity", () => {
  assert.equal(baseInterval(NOW - 2 * HOUR, NOW), DAY);
  assert.equal(baseInterval(NOW - 3 * DAY, NOW), 3 * DAY);
  assert.equal(baseInterval(NOW - 10 * DAY, NOW), 7 * DAY);
  assert.equal(baseInterval(NOW - 60 * DAY, NOW), 14 * DAY);
  assert.equal(baseInterval(null, NOW), 14 * DAY, "never seen in an upload");
});

test("the wait doubles with every look that finds nothing new, up to a cap", () => {
  const seen = NOW - HOUR;
  assert.deepEqual([0, 1, 2, 3, 4, 9].map((n) => characterInterval(seen, n, NOW) / DAY), [1, 2, 4, 8, 14, 14]);
  const away = NOW - 60 * DAY;
  assert.equal(characterInterval(away, 0, NOW), 14 * DAY);
  assert.equal(characterInterval(away, 5, NOW), 30 * DAY, "a month at most");
});

test("the own client's profile is the fresher source: twice the wait", () => {
  assert.equal(characterInterval(NOW - HOUR, 0, NOW, true), 2 * DAY);
});

test("the portrait changes with the looks only: weekly, monthly for players gone for a month", () => {
  assert.equal(portraitInterval(NOW - DAY, NOW), 7 * DAY);
  assert.equal(portraitInterval(NOW - 40 * DAY, NOW), 30 * DAY);
  assert.equal(portraitInterval(null, NOW), 30 * DAY);
});

test("the server's times are read as UTC", () => {
  assert.equal(parseSeen("2026-10-11 12:00:00"), Date.UTC(2026, 9, 11, 12));
  assert.equal(parseSeen("2026-10-11T12:00:00.000Z"), Date.UTC(2026, 9, 11, 12));
  assert.equal(parseSeen(null), null);
  assert.equal(parseSeen("nonsense"), null);
});
