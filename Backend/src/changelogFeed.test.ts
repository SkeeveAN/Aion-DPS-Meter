import { test } from "node:test";
import assert from "node:assert/strict";
import { buildChangelogFeed, changelogFeed, isVersion, shortenText } from "./changelogFeed.js";

const releases = [
  { version: "Unreleased", date: "2026-11-01", items: [{ text: "Client: something not out yet", areas: ["Client"] }] },
  {
    version: "1.2.0",
    date: "2026-10-03",
    items: [
      { text: "Website: only on the site", areas: ["Website"] },
      { text: "Tools: a new window (the long part of the entry goes on and on)", areas: ["Client"] },
      { text: "Fix: a crash is gone", areas: ["Client"] },
      { text: "a new window and a crash fix, summed up", areas: ["Client"] },
    ],
  },
  { version: "1.1.0", date: "2026-10-02", items: [{ text: "Only change of this release", areas: ["Client"] }] },
  { version: "1.0.1", date: "2026-10-01", items: [{ text: "Backend: no client change", areas: ["Backend"] }] },
  { version: "1.0.0", date: "2026-09-30", items: [{ text: "Old", areas: ["Client"] }] },
];
const german = { "Fix: a crash is gone": "Fix: Ein Absturz ist weg" };
const shorts = { "Tools: a new window (the long part of the entry goes on and on)": { en: "Tools: a new window", de: "Tools: ein neues Fenster" } };

test("feed: versions after from up to to, newest first, without the release line, client entries only", () => {
  const feed = buildChangelogFeed(releases, german, shorts, "1.0.0", undefined, "en");
  assert.deepEqual(feed.map((v) => v.version), ["1.2.0", "1.1.0"]);
  assert.deepEqual(feed[0].items, ["Tools: a new window", "Fix: a crash is gone"]);
  assert.deepEqual(feed[1].items, ["Only change of this release"]);
  assert.deepEqual(buildChangelogFeed(releases, german, shorts, "1.0.0", "1.1.0", "en").map((v) => v.version), ["1.1.0"]);
  assert.deepEqual(buildChangelogFeed(releases, german, shorts, "1.2.0", undefined, "en"), []);
});

test("feed: German short text, German full text as the fallback", () => {
  const feed = buildChangelogFeed(releases, german, shorts, "1.1.0", undefined, "de");
  assert.deepEqual(feed[0].items, ["Tools: ein neues Fenster", "Fix: Ein Absturz ist weg"]);
});

test("feed: an empty short text hides the entry", () => {
  const hidden = { ...shorts, "Fix: a crash is gone": { en: "", de: "" } };
  assert.deepEqual(buildChangelogFeed(releases, german, hidden, "1.1.0", undefined, "en")[0].items, ["Tools: a new window"]);
});

test("feed: a bad version gives an empty list, shortenText cuts at a clause or a word", () => {
  assert.deepEqual(buildChangelogFeed(releases, german, shorts, "x", undefined, "en"), []);
  assert.equal(isVersion("1.18.2"), true);
  assert.equal(isVersion("1.18"), false);
  assert.equal(shortenText("tools: a window (more)"), "Tools: a window");
  assert.equal(shortenText("a ".repeat(100)).endsWith("…"), true);
  assert.ok(shortenText("word ".repeat(40)).length <= 112);
});

test("feed: the site's own files give the 1.18.2 changes with short lines", () => {
  const feed = changelogFeed("1.18.1", "1.18.2", "de");
  assert.equal(feed.length, 1);
  assert.equal(feed[0].version, "1.18.2");
  assert.ok(feed[0].items.length >= 5);
  assert.ok(feed[0].items.every((line) => line.length <= 110));
});
