import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

// DATABASE_PATH must be set before db/client.ts is first imported (see matching/merge.test.ts).
process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-buildstats-test-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { players, playerProfiles, playerNcCharacter } = await import("./db/schema.js");
const { groupSize, typicalBuild, refreshBuildStats, readAnswer, optimalCharacter, medianLevel, factsOf, MIN_CELL } = await import("./buildStats.js");
const { buildStatsRoutes } = await import("./routes/buildStats.js");

const app = Fastify({ logger: false });
await app.register(buildStatsRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

let counter = 0;
interface Spec {
  classId?: number;
  power?: number | null;
  level?: number;
  updatedAt?: string;
  skillLevel?: number;
  item?: number;
  nodes?: number[];
  stones?: number[];
}
/** One character with a profile; every list is a small JSON like the real ones. */
function character(spec: Spec): number {
  counter++;
  const id = db.insert(players).values({ name: `Bt${counter}`, nameNormalized: `bt${counter}`, slug: `bt${counter}-test` }).returning({ id: players.id }).get().id;
  db.insert(playerProfiles)
    .values({
      playerId: id,
      source: "nc",
      level: spec.level ?? 45,
      classId: spec.classId ?? 3,
      updatedAt: spec.updatedAt ?? "2026-11-15 10:00:00",
      skillsJson: JSON.stringify([{ id: 14010000, level: spec.skillLevel ?? 10, ...(spec.stones ? { stones: spec.stones } : {}) }]),
      gearJson: JSON.stringify([{ slot: 1, itemId: spec.item ?? 100 }]),
      daevanionJson: JSON.stringify([{ board: 31, nodes: spec.nodes ?? [1, 2, 3] }]),
    })
    .run();
  if (spec.power !== null) {
    db.insert(playerNcCharacter).values({ playerId: id, status: "ok", fetchedAt: 0, nextTryAt: 0, combatPower: spec.power ?? 1000 + counter }).run();
  }
  return id;
}

test("group sizes: Top groups come from the ranked part, 'all' is everybody", () => {
  assert.equal(groupSize("top10", 50, 80), 10);
  assert.equal(groupSize("top10", 4, 80), 4);
  assert.equal(groupSize("top25", 50, 80), 25);
  assert.equal(groupSize("pct10", 50, 80), 5);
  assert.equal(groupSize("pct10", 3, 80), 1);
  assert.equal(groupSize("pct10", 0, 80), 0);
  assert.equal(groupSize("all", 50, 80), 80);
});

test("facts of a profile row: plain or gzip, junk left out", () => {
  const facts = factsOf({
    skills: JSON.stringify([{ id: 5, level: 7, stones: [0, 2] }, { id: 6, level: 0 }, { nope: true }]),
    daevanion: JSON.stringify([{ board: 31, nodes: [3, 1, 3] }, { board: 32, nodes: [] }]),
    gear: JSON.stringify([{ slot: 1, itemId: 9, godstone: 4, stones: [{ stat: 12, tier: 2 }, { stat: 0, tier: 1 }] }]),
    pantheon: JSON.stringify({ presets: [{ preset: 1, slots: [{ slot: 1, id: 63 }] }] }),
    titles: "not json",
    pet: 0,
  });
  assert.deepEqual(facts.skills, [{ id: 5, level: 7, stones: [0, 2] }]);
  assert.deepEqual(facts.boards, [{ board: 31, nodes: [1, 3] }]);
  assert.deepEqual(facts.gear, [{ slot: 1, itemId: 9, godstone: 4, stones: [{ stat: 12, tier: 2 }] }]);
  assert.deepEqual(facts.pantheon, [{ slot: 1, id: 63 }]);
  assert.deepEqual(facts.titles, []);
  assert.equal(facts.pet, null);
});

test("typical build: the densest cluster, never a lone board", () => {
  const base = Array.from({ length: 60 }, (_, i) => i + 1);
  const close = Array.from({ length: 12 }, (_, i) => ({ nodes: [...base.slice(0, 58), 100 + i % 3], w: 1 }));
  const far = Array.from({ length: 4 }, (_, i) => ({ nodes: Array.from({ length: 60 }, (_, k) => 1000 + i * 100 + k), w: 1 }));
  const found = typicalBuild([...close, ...far]);
  assert.ok(found);
  assert.equal(found.n, 12);
  assert.ok(found.nodes.includes(1) && found.nodes.includes(58) && !found.nodes.includes(1000));
  // Everybody different: nothing is reported.
  const unique = Array.from({ length: 15 }, (_, i) => ({ nodes: Array.from({ length: 60 }, (_, k) => i * 1000 + k), w: 1 }));
  assert.equal(typicalBuild(unique), null);
  // Too few boards.
  assert.equal(typicalBuild(close.slice(0, 5)), null);
});

test("refresh: ranking by power, groups, shares, cells that are too small, 'too little data'", async () => {
  // 40 characters of class 3: power rises with the index; the 10 strongest have skill level 20 and item 200, the rest level 10 and item 100.
  for (let i = 0; i < 40; i++) {
    character({ power: 1000 + i, skillLevel: i >= 30 ? 20 : 10, item: i >= 30 ? 200 : 100, nodes: i >= 30 ? [1, 2, 9] : [1, 2, 3] });
  }
  // Two characters below the maximum level, one deleted-like empty one and a class with only four characters.
  character({ power: 9_999_999, level: 30, skillLevel: 1 });
  for (let i = 0; i < 4; i++) {
    character({ classId: 5, power: 500 + i });
  }
  await refreshBuildStats(sqlite, [3, 5]);

  const top10 = readAnswer(sqlite, 3, "top10")!;
  assert.equal(top10.size, 10);
  const skill = top10.questions.find((q) => q.topic === "skill_level" && q.key === "14010000")!;
  assert.equal(skill.base, 10);
  assert.deepEqual(skill.options.map((o) => [o.option, o.n, o.share]), [["20", 10, 1]]);
  assert.equal(medianLevel(skill), 20);

  const all = readAnswer(sqlite, 3, "all")!;
  assert.equal(all.size, 40, "the level-30 character does not count");
  const levels = all.questions.find((q) => q.topic === "skill_level")!;
  assert.deepEqual(levels.options.map((o) => [o.option, o.share]), [["10", 0.75], ["20", 0.25]]);
  const items = all.questions.find((q) => q.topic === "gear_item")!;
  assert.equal(items.options[0]!.option, "100");

  // top25 holds 10 strong and 15 weaker ones: item 100 wins with 60 %.
  const top25 = readAnswer(sqlite, 3, "top25")!;
  assert.equal(top25.questions.find((q) => q.topic === "gear_item")!.options[0]!.share, 0.6);

  // A class with four characters: every question says "too little data" and carries no numbers.
  const small = readAnswer(sqlite, 5, "all")!;
  assert.equal(small.tooFew, true);
  assert.ok(small.questions.every((q) => q.tooFew && q.options.length === 0));
});

test("cells shared by fewer than three characters are not stored", async () => {
  for (let i = 0; i < 12; i++) {
    character({ classId: 2, power: 2000 + i, item: i < 2 ? 777 : 100 });
  }
  await refreshBuildStats(sqlite, [2]);
  const stored = sqlite.prepare("select count(*) as c from build_stats where class_id = 2 and topic = 'gear_item' and option = '777'").get() as { c: number };
  assert.equal(stored.c, 0, `${MIN_CELL - 1} characters with item 777 must not show up`);
  const kept = sqlite.prepare("select count(*) as c from build_stats where class_id = 2 and topic = 'gear_item' and option = '100'").get() as { c: number };
  assert.ok(kept.c > 0);
});

test("older data counts less after the patch date", async () => {
  for (let i = 0; i < 12; i++) {
    character({ classId: 4, power: 3000 + i, skillLevel: i < 6 ? 10 : 20, updatedAt: i < 6 ? "2026-10-01 00:00:00" : "2026-11-18 00:00:00" });
  }
  await refreshBuildStats(sqlite, [4]);
  const plain = readAnswer(sqlite, 4, "all")!.questions.find((q) => q.topic === "skill_level")!;
  assert.equal(plain.options.find((o) => o.option === "10")!.share, 0.5);
  await refreshBuildStats(sqlite, [4], { patchDate: "2026-11-01" });
  const weighted = readAnswer(sqlite, 4, "all")!.questions.find((q) => q.topic === "skill_level")!;
  assert.equal(Math.round(weighted.options.find((o) => o.option === "10")!.share * 100), 20, "6 old at weight 0.25 against 6 new at weight 1");
  assert.equal(weighted.options.find((o) => o.option === "10")!.n, 6, "the count itself is not weighted");
});

test("stones of the skills and the optimal character", async () => {
  for (let i = 0; i < 12; i++) {
    character({ classId: 6, power: 4000 + i, skillLevel: 14 + (i % 3), stones: [i < 9 ? 2 : 4, 1] });
  }
  await refreshBuildStats(sqlite, [6]);
  const answer = readAnswer(sqlite, 6, "all")!;
  const optimal = optimalCharacter(answer);
  assert.equal(optimal.size, 12);
  assert.deepEqual(optimal.stones.map((s) => [s.skill, s.slot, s.choice]), [[14010000, 1, 2], [14010000, 2, 1]]);
  assert.equal(optimal.skills[0]!.level, 15);
  assert.equal(optimal.gear[0]!.itemId, 100);
  assert.equal(optimal.boards[0]!.board, 31);
  assert.deepEqual(optimal.boards[0]!.commonNodes.map((n) => n.node), [1, 2, 3]);
});

test("the route: not built, bad request, answer, optimal; no player appears in it", async () => {
  assert.equal((await app.inject("/api/build-stats/8")).statusCode, 404);
  assert.equal((await app.inject("/api/build-stats/12")).statusCode, 400);
  assert.equal((await app.inject("/api/build-stats/3?group=top7")).statusCode, 400);
  assert.equal((await app.inject("/api/build-stats/3?topic=hair")).statusCode, 400);

  const res = await app.inject("/api/build-stats/3?group=top10&topic=gear_item");
  assert.equal(res.statusCode, 200);
  assert.match(String(res.headers["cache-control"]), /max-age=300/);
  const body = res.json() as { size: number; questions: { topic: string }[] };
  assert.equal(body.size, 10);
  assert.ok(body.questions.every((q) => q.topic === "gear_item"));

  const optimal = await app.inject("/api/build-stats/3/optimal?group=all");
  assert.equal(optimal.statusCode, 200);
  assert.equal((optimal.json() as { size: number }).size, 40);
  const text = res.body + optimal.body;
  assert.ok(!/Bt\d+|playerId|player_id/.test(text), "no character name or id in a group result");

  const index = (await app.inject("/api/build-stats")).json() as { classes: unknown[]; groups: string[]; meta: { classId: number }[] };
  assert.equal(index.classes.length, 8);
  assert.ok(index.meta.some((m) => m.classId === 3));
});

test("one character against the Top 10 of its class", async () => {
  // A weak character of class 3 (the answers of class 3 were built above from 40 others): low skill level, another piece, a thin board.
  const id = character({ classId: 3, power: 10, skillLevel: 5, item: 999, nodes: [1, 2] });
  const res = await app.inject(`/api/players/${id}/build-compare?group=top10`);
  assert.equal(res.statusCode, 200);
  const c = res.json() as import("./buildStats.js").Comparison;
  assert.equal(c.size, 10);
  assert.equal(c.player.ranked, 41);
  assert.equal(c.player.rank, 41);
  assert.equal(c.player.inGroup, false);
  assert.deepEqual(c.skills, [{ skill: 14010000, own: 5, median: 20, diff: -15 }]);
  assert.equal(c.gear[0]!.same, false);
  assert.equal(c.gear[0]!.top, 200);
  assert.equal(c.gear[0]!.ownShare, 0);
  assert.deepEqual(c.boards[0]!.missing, [3, 9].filter((n) => n === 9), "node 9 is held by the whole Top 10, node 3 by none of them");
  assert.ok(!/Bt\d+/.test(res.body), "no name in the result");

  // A character of the strongest ten is in the group.
  const strong = sqlite.prepare("select player_id as id from player_nc_character where combat_power = 1039").get() as { id: number };
  const own = (await app.inject(`/api/players/${strong.id}/build-compare?group=top10`)).json() as import("./buildStats.js").Comparison;
  assert.equal(own.player.rank, 1);
  assert.equal(own.player.inGroup, true);
  assert.equal(own.skills[0]!.diff, 0);
  assert.equal(own.gear[0]!.same, true);

  assert.equal((await app.inject("/api/players/99999/build-compare")).statusCode, 404);
  assert.equal((await app.inject("/api/players/not_valid/build-compare")).statusCode, 400);
  assert.equal((await app.inject(`/api/players/${id}/build-compare?group=top3`)).statusCode, 400);
});
