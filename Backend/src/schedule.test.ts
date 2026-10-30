import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";

const { scheduleBody } = await import("./routes/schedule.js");

test("the shipped timetable is served as it is: every event has an id, names and windows or dates", () => {
  const table = JSON.parse(scheduleBody()) as { timeZone: string; events: { id: string; names: Record<string, string>; windows: unknown[]; always?: boolean }[] };
  assert.equal(table.timeZone, "Europe/Berlin");
  const ids = table.events.map((e) => e.id);
  assert.equal(new Set(ids).size, ids.length);
  for (const id of ["arena_of_strategy", "abyss_raid", "spacetime_rift", "executor_bosses", "guardian_lord_nahma", "shugo_festival", "beritra_air_raid"]) {
    assert.ok(ids.includes(id), id);
  }
  for (const e of table.events) {
    assert.ok(e.names.en, e.id);
    assert.ok(e.always || e.windows.length > 0, e.id);
  }
});

test("a file that is not a timetable is refused", () => {
  const dir = mkdtempSync(path.join(tmpdir(), "schedule-"));
  const bad = path.join(dir, "bad.json");
  writeFileSync(bad, JSON.stringify({ nope: 1 }));
  assert.throws(() => scheduleBody(null, bad));
  assert.throws(() => scheduleBody(null, path.join(dir, "missing.json")));
});

test("the region gives the rift its own clock and leaves the other events on the European one", () => {
  type E = { id: string; timeZone?: string; zones?: unknown };
  const zoneOf = (region: string | null, id: string) => (JSON.parse(scheduleBody(region)) as { events: E[] }).events.find((e) => e.id === id);
  assert.equal(zoneOf("nae", "spacetime_rift")?.timeZone, "America/New_York");
  assert.equal(zoneOf("naw", "spacetime_rift")?.timeZone, "America/Los_Angeles");
  assert.equal(zoneOf("eu", "spacetime_rift")?.timeZone, "Europe/Berlin");
  assert.equal(zoneOf("nae", "arena_of_strategy")?.timeZone, undefined); // no verified clock: the file's zone applies
  assert.equal(zoneOf("nae", "spacetime_rift")?.zones, undefined);
  // no region, an unknown one: the European table, as it is
  assert.equal(zoneOf(null, "spacetime_rift")?.timeZone, undefined);
  assert.equal(zoneOf("mars", "spacetime_rift")?.timeZone, undefined);
  assert.equal((JSON.parse(scheduleBody("asia")) as { region?: string }).region, "asia");
});
