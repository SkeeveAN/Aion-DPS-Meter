import { test } from "node:test";
import assert from "node:assert/strict";
import { parseWindow, plainText, stateOf } from "./maintenance.js";

const TEXT =
  "Hello Daevas, We will be performing our scheduled game server maintenance soon. Details: When: October 20, 2026 at 23h30 PDT / October 21, 2026 at 08h30 CEST ​Duration: 5.5 hours Affected Services: Game servers";

test("the window of NC's notice: start in UTC, length from the duration", () => {
  const w = parseWindow(TEXT);
  assert.ok(w);
  assert.equal(new Date(w.start).toISOString(), "2026-10-21T06:30:00.000Z");
  assert.equal(new Date(w.end).toISOString(), "2026-10-21T12:00:00.000Z");
});

test("minutes, and a notice without a date yet", () => {
  const w = parseWindow("When: October 13 14:00 PDT / 23:00 CEST Duration: 30 minutes");
  assert.equal(w, null); // no year: not a game server notice of this form
  const m = parseWindow("When: October 14, 2026 at 08h30 CEST Duration: 90 minutes");
  assert.ok(m);
  assert.equal(m.end - m.start, 90 * 60_000);
  assert.equal(parseWindow("When: TBD Duration: TBD"), null);
});

test("state: scheduled within three days, active inside the window, nothing after it", () => {
  const w = { start: Date.UTC(2026, 9, 21, 6, 30), end: Date.UTC(2026, 9, 21, 12, 0) };
  assert.equal(stateOf(w, w.start - 3_600_000), "scheduled");
  assert.equal(stateOf(w, w.start - 4 * 86_400_000), null);
  assert.equal(stateOf(w, w.start + 1), "active");
  assert.equal(stateOf(w, w.end), null);
});

test("plain text drops tags and entities", () => {
  assert.equal(plainText("<p>Hello&nbsp;Daevas</p><p>When: x</p>"), "Hello Daevas When: x");
});

import { regionOfServerId } from "./serverStatus.js";

test("region of a server id: the second digit", () => {
  assert.equal(regionOfServerId(1304), "eu");
  assert.equal(regionOfServerId(2321), "eu");
  assert.equal(regionOfServerId(1405), "latam");
  assert.equal(regionOfServerId(1101), "nae");
  assert.equal(regionOfServerId(2506), "asia");
  assert.equal(regionOfServerId(2201), "naw");
  assert.equal(regionOfServerId(1004), null);
  assert.equal(regionOfServerId(null), null);
});
