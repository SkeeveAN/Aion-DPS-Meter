import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { mergeCampaigns, parseDropsNotice, statusOf } from "./twitchDrops.js";

const fixture = (name: string) => (JSON.parse(readFileSync(new URL(`./fixtures/${name}`, import.meta.url), "utf8")) as { article: { content: { content: string } } }).article.content.content;

test("global notice: two campaigns with day ranges and every reward row", () => {
  const { claimBy, campaigns } = parseDropsNotice(fixture("nc-twitch-drops-global.json"), "g", "2026-09-30T15:00:00.000Z");
  assert.equal(campaigns.length, 2);
  const [early, launch] = campaigns;
  assert.equal(early.name, "Advanced Access Rewards");
  assert.equal(early.dateOnly, true);
  assert.equal(early.startAt, "2026-09-30T07:00:00.000Z");
  assert.equal(early.endAt, "2026-10-05T06:59:00.000Z");
  assert.deepEqual(early.rewards.map((r) => [r.minutes, r.qty]), [[15, 1], [30, 1], [45, 2], [60, 3], [120, 5000], [240, 10], [360, 1], [480, 1]]);
  assert.equal(launch.name, "Global Launch Rewards");
  assert.equal(launch.endAt, "2026-10-17T06:59:00.000Z");
  assert.deepEqual(launch.rewards.map((r) => r.minutes), [60, 120, 180, 240, 360, 480, 600]);
  assert.equal(launch.rewards[1].name, "Skin Chest x10 (Bound)");
  assert.equal(launch.rewards[1].qty, 1);
  assert.equal(claimBy, "2026-10-31T06:59:00.000Z");
});

test("war for atreia notice: three periods, explicit times, a TBD start falls back to the heading's days", () => {
  const { campaigns } = parseDropsNotice(fixture("nc-twitch-drops-war-for-atreia.json"), "w", "2026-10-02T19:00:00.000Z");
  assert.equal(campaigns.length, 3);
  assert.deepEqual(campaigns.map((c) => c.part), [1, 2, 3]);
  assert.ok(campaigns.every((c) => c.name === "War For Atreia"));
  assert.equal(campaigns[0].startAt, "2026-10-02T19:00:00.000Z");
  assert.equal(campaigns[0].endAt, "2026-10-05T06:59:00.000Z");
  assert.equal(campaigns[0].dateOnly, false);
  assert.equal(campaigns[1].startAt, "2026-10-07T17:00:00.000Z");
  assert.equal(campaigns[2].dateOnly, true);
  assert.equal(campaigns[2].startAt, "2026-10-12T07:00:00.000Z");
  assert.deepEqual(campaigns[2].rewards.map((r) => [r.name, r.qty]), [["Odyle Energy (Bound)", 4], ["Skin Chest x10 (Bound)", 1], ["Resurrection Spiritstone (Bound)", 15], ["Leather Crop Fit", 1]]);
});

test("halloween notice: 'All Channels' without a colon, the period comes from the Event Schedule, the name from the title", () => {
  const { claimBy, campaigns } = parseDropsNotice(fixture("nc-twitch-drops-halloween.json"), "h", "2026-10-28T14:00:00.000Z", "Global Halloween Twitch Drops: October 28 - November 3 PT");
  assert.equal(campaigns.length, 1);
  const [c] = campaigns;
  assert.equal(c.name, "Halloween");
  assert.equal(c.startAt, "2026-10-28T17:00:00.000Z");
  assert.equal(c.endAt, "2026-11-04T07:59:00.000Z");
  assert.equal(c.dateOnly, false);
  assert.deepEqual(c.rewards.map((r) => [r.minutes, r.name, r.qty]), [[60, "Resurrection Spiritstone (Bound)", 15], [120, "Appearance Change Voucher (Bound)", 1], [240, "Customization Voucher (Bound)", 3], [360, "Odyle Energy (Bound)", 4], [480, "Chubby Panda (Helm)", 1]]);
  assert.equal(claimBy, "2026-11-18T07:59:00.000Z");
});

test("a notice without tables gives no campaigns", () => {
  assert.deepEqual(parseDropsNotice("<p>Hello Daevas, last chance to earn rewards.</p>", "x", "2026-10-04T06:00:00.000Z").campaigns, []);
});

test("the reminder repeats two tables: merged, the version with clock times wins and nothing is listed twice", () => {
  const notices = [
    ["nc-twitch-drops-global.json", "2026-09-30T15:00:00.000Z"],
    ["nc-twitch-drops-war-for-atreia.json", "2026-10-02T19:00:00.000Z"],
    ["nc-twitch-drops-reminder.json", "2026-10-04T06:00:00.000Z"],
  ].map(([file, postedAt], i) => ({ postedAt, campaigns: parseDropsNotice(fixture(file), `n${i}`, postedAt).campaigns }));
  const merged = mergeCampaigns(notices);
  assert.deepEqual(merged.map((c) => `${c.name}${c.part ? ` ${c.part}` : ""}`), ["Advanced Access Rewards", "War For Atreia 1", "Global Launch Rewards", "War For Atreia 2", "War For Atreia 3"]);
  assert.equal(merged[0].startAt, "2026-09-30T13:00:00.000Z");
  assert.equal(merged[0].dateOnly, false);
});

test("status for the meter icon: running while one is live, the latest start grows with a new campaign, nothing running after the end", () => {
  const notices = [
    ["nc-twitch-drops-global.json", "2026-09-30T15:00:00.000Z"],
    ["nc-twitch-drops-war-for-atreia.json", "2026-10-02T19:00:00.000Z"],
  ].map(([file, postedAt], i) => ({ postedAt, campaigns: parseDropsNotice(fixture(file), `n${i}`, postedAt).campaigns }));
  const merged = mergeCampaigns(notices);
  const during = statusOf(merged, Date.parse("2026-10-11T18:00:00Z"));
  assert.equal(during.running, true);
  assert.equal(during.latest, Date.parse("2026-10-07T17:00:00Z") / 1000); // "War For Atreia" part 2 is the newest started one
  assert.equal(during.endsAt, "2026-10-17T06:59:00.000Z");
  // part 3 starts on 12 Oct (Pacific time): the latest start moves on, which is what makes the badge light up again
  assert.equal(statusOf(merged, Date.parse("2026-10-12T08:00:00Z")).latest, Date.parse("2026-10-12T07:00:00Z") / 1000);
  assert.deepEqual(statusOf(merged, Date.parse("2026-10-20T00:00:00Z")).running, false);
  assert.deepEqual(statusOf([], Date.now()), { running: false, latest: null, endsAt: null });
});
