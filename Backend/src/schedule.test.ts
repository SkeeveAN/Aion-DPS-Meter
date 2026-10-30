import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";

const { scheduleBody, SCHEDULE_FILE } = await import("./routes/schedule.js");

test("the shipped timetable is served as it is: every event has an id, names and windows or dates", () => {
  const table = JSON.parse(scheduleBody(SCHEDULE_FILE)) as { timeZone: string; events: { id: string; names: Record<string, string>; windows: unknown[]; always?: boolean }[] };
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
  assert.throws(() => scheduleBody(bad));
  assert.throws(() => scheduleBody(path.join(dir, "missing.json")));
});
