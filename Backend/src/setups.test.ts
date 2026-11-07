import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

// DATABASE_PATH must be set before db/client.ts is first imported (see matching/merge.test.ts).
process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-setups-test-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { players, profileSetups, setupVotes } = await import("./db/schema.js");
const { upsertProfile, profileSchema } = await import("./profile.js");
const { eq } = await import("drizzle-orm");
const { normaliseSetup, setupHash, recordSetup, setupsOfPlayer, addThumb, removeThumb, moveThumb, MAX_THUMBS_PER_ADDRESS } = await import("./setups.js");
const { setupRoutes } = await import("./routes/setups.js");

const app = Fastify({ logger: false });
await app.register(setupRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

let counter = 0;
function newPlayer(): number {
  counter++;
  return db
    .insert(players)
    .values({ name: `Tester${counter}`, nameNormalized: `tester${counter}`, slug: `tester${counter}-test` })
    .returning({ id: players.id })
    .get().id;
}

const boards = (...lists: number[][]) => JSON.stringify(lists.map((nodes, i) => ({ board: 10 + i, nodes })));
const skillList = (levels: Record<number, number>, extra: Record<string, unknown> = {}) =>
  JSON.stringify(Object.entries(levels).map(([id, baseLevel]) => ({ id: Number(id), level: baseLevel + 2, baseLevel, ...extra })));

const voterA = "a".repeat(32);
const voterB = "b".repeat(32);

test("the state of a board set does not depend on the order of the lists", () => {
  assert.equal(setupHash("board", boards([3, 1, 2], [9, 8])), setupHash("board", JSON.stringify([{ board: 11, nodes: [8, 9] }, { board: 10, nodes: [2, 3, 1] }])));
  assert.notEqual(setupHash("board", boards([1, 2, 3])), setupHash("board", boards([1, 2, 4])));
  assert.equal(normaliseSetup("board", "[]"), null);
  assert.equal(normaliseSetup("board", "not json"), null);
});

test("the state of a skill build is what was chosen: gear moves the effective level, not the state", () => {
  const base = skillList({ 10000: 5, 20000: 3 });
  const gearMoved = JSON.stringify(JSON.parse(base).map((s: { level: number }) => ({ ...s, level: s.level + 3 })));
  assert.equal(setupHash("skills", base), setupHash("skills", gearMoved));
  assert.notEqual(setupHash("skills", base), setupHash("skills", skillList({ 10000: 6, 20000: 3 })));
  // a skill that is not learned does not count
  assert.equal(setupHash("skills", skillList({ 10000: 5, 30000: 0 })), setupHash("skills", skillList({ 10000: 5 })));
  assert.equal(normaliseSetup("skills", skillList({ 10000: 0 })), null);
});

test("the same state twice is one row; a new state without a thumb overwrites in place", () => {
  const id = newPlayer();
  const first = recordSetup(id, "board", boards([1, 2]), 1)!;
  assert.equal(recordSetup(id, "board", boards([2, 1]), 1), first);
  const second = recordSetup(id, "board", boards([1, 2, 3]), 1)!;
  assert.equal(second, first, "an unrated state is overwritten, not kept");
  assert.equal(db.select().from(profileSetups).where(eq(profileSetups.playerId, id)).all().length, 1);
});

test("a state with thumbs is kept when the character changes; the new state starts at zero", () => {
  const id = newPlayer();
  const old = recordSetup(id, "board", boards([1, 2]), 1)!;
  assert.deepEqual(addThumb(old, "hash-a", "ip-1"), { ok: true, thumbs: 1, voted: true });
  const fresh = recordSetup(id, "board", boards([1, 2, 3]), 1)!;
  assert.notEqual(fresh, old);
  const state = setupsOfPlayer(id, "hash-a").board;
  assert.equal(state.current!.id, fresh);
  assert.equal(state.current!.thumbs, 0);
  assert.equal(state.current!.voted, false);
  assert.deepEqual(state.earlier.map((e) => [e.id, e.thumbs, e.voted]), [[old, 1, true]]);
  // a reader that is not the voter sees the thumb but not as their own
  assert.equal(setupsOfPlayer(id, "hash-b").board.earlier[0].voted, false);
});

test("going back to an earlier rated state makes its row current again, thumbs included", () => {
  const id = newPlayer();
  const old = recordSetup(id, "skills", skillList({ 10000: 5 }), 1)!;
  addThumb(old, "hash-a", "ip-1");
  const fresh = recordSetup(id, "skills", skillList({ 10000: 6 }), 1)!;
  assert.notEqual(fresh, old);
  assert.equal(recordSetup(id, "skills", skillList({ 10000: 5 }), 1), old);
  const state = setupsOfPlayer(id, null).skills;
  assert.equal(state.current!.id, old);
  assert.equal(state.current!.thumbs, 1);
  assert.equal(state.earlier.length, 0, "the unrated state in between is gone");
});

test("a browser has one thumb per state, and taking it back works", () => {
  const id = newPlayer();
  const setup = recordSetup(id, "board", boards([1]), 1)!;
  assert.deepEqual(addThumb(setup, "hash-a", "ip-1"), { ok: true, thumbs: 1, voted: true });
  assert.deepEqual(addThumb(setup, "hash-a", "ip-1"), { ok: true, thumbs: 1, voted: true }, "a second thumb of the same browser does nothing");
  assert.deepEqual(addThumb(setup, "hash-b", "ip-2"), { ok: true, thumbs: 2, voted: true });
  assert.deepEqual(removeThumb(setup, "hash-a"), { ok: true, thumbs: 1, voted: false });
  assert.deepEqual(removeThumb(setup, "hash-a"), { ok: true, thumbs: 1, voted: false }, "taking back twice changes nothing");
  assert.equal(db.select().from(profileSetups).where(eq(profileSetups.id, setup)).get()!.thumbs, 1);
});

test("clearing the browser's storage does not give an unlimited number of thumbs from one address", () => {
  const id = newPlayer();
  const setup = recordSetup(id, "board", boards([1]), 1)!;
  for (let i = 0; i < MAX_THUMBS_PER_ADDRESS; i++) {
    assert.equal(addThumb(setup, `fresh-browser-${i}`, "same-address").ok, true);
  }
  assert.deepEqual(addThumb(setup, "fresh-browser-x", "same-address"), { ok: false, error: "address_limit" });
  assert.equal(addThumb(setup, "fresh-browser-x", "other-address").ok, true);
});

test("an earlier state takes no new thumbs, and when its last thumb goes it is deleted", () => {
  const id = newPlayer();
  const old = recordSetup(id, "board", boards([1]), 1)!;
  addThumb(old, "hash-a", "ip-1");
  recordSetup(id, "board", boards([1, 2]), 1);
  assert.deepEqual(addThumb(old, "hash-b", "ip-2"), { ok: false, error: "superseded" });
  assert.deepEqual(removeThumb(old, "hash-a"), { ok: true, thumbs: 0, voted: false });
  assert.equal(db.select().from(profileSetups).where(eq(profileSetups.id, old)).get(), undefined);
  assert.equal(addThumb(old, "hash-a", "ip-1").ok, false);
});

test("a thumb moves from an earlier state to the current one", () => {
  const id = newPlayer();
  const old = recordSetup(id, "skills", skillList({ 10000: 5 }), 1)!;
  addThumb(old, "hash-a", "ip-1");
  addThumb(old, "hash-b", "ip-2");
  const fresh = recordSetup(id, "skills", skillList({ 10000: 6 }), 1)!;
  const moved = moveThumb(old, "hash-a", "ip-1");
  assert.deepEqual(moved, { ok: true, thumbs: 1, voted: true, toId: fresh, fromThumbs: 1 });
  const state = setupsOfPlayer(id, "hash-a").skills;
  assert.equal(state.current!.thumbs, 1);
  assert.equal(state.current!.voted, true);
  assert.equal(state.earlier[0].thumbs, 1);
  assert.equal(state.earlier[0].voted, false);
  assert.deepEqual(moveThumb(old, "hash-a", "ip-1"), { ok: false, error: "nothing_to_transfer" }, "the thumb is not there any more");
  assert.deepEqual(moveThumb(99999, "hash-a", "ip-1"), { ok: false, error: "not_found" });
});

test("a stored profile is recorded when it is written, boards and skills apart", () => {
  const id = newPlayer();
  const profile = {
    source: "self" as const,
    classId: 1,
    classBits: 2,
    gear: [],
    skills: [{ id: 10000, level: 5, baseLevel: 5 }],
    daevanion: [{ board: 10, nodes: [1, 2, 3] }],
  };
  upsertProfile(id, profileSchema.parse(profile));
  const state = setupsOfPlayer(id, null);
  assert.ok(state.board.current);
  assert.ok(state.skills.current);
  const board = state.board.current!.id;
  const skills = state.skills.current!.id;
  // the same upload again changes nothing; a changed board leaves the skill build alone
  upsertProfile(id, profileSchema.parse(profile));
  assert.equal(setupsOfPlayer(id, null).board.current!.id, board);
  upsertProfile(id, profileSchema.parse({ ...profile, daevanion: [{ board: 10, nodes: [1, 2, 3, 4] }] }));
  assert.equal(setupsOfPlayer(id, null).skills.current!.id, skills);
});

test("the routes: read, thumb, take back, move; a reader without an id cannot vote", async () => {
  const id = newPlayer();
  const slug = `tester${counter}-test`;
  const own = { source: "self" as const, classId: 1, classBits: 2, gear: [], skills: [], daevanion: [{ board: 10, nodes: [1, 2, 3] }] };
  upsertProfile(id, profileSchema.parse(own));
  const setup = setupsOfPlayer(id, null).board.current!.id;
  const headers = { "x-voter": voterA };

  const read = await app.inject({ method: "GET", url: `/api/players/${slug}/setups`, headers });
  assert.equal(read.statusCode, 200);
  assert.equal(read.json().board.current.id, setup);
  assert.equal(read.json().maxPerAddress, MAX_THUMBS_PER_ADDRESS);

  assert.equal((await app.inject({ method: "POST", url: `/api/setups/${setup}/thumb` })).statusCode, 400, "no id, no thumb");
  assert.equal((await app.inject({ method: "POST", url: `/api/setups/${setup}/thumb`, headers: { "x-voter": "short" } })).statusCode, 400);
  const put = await app.inject({ method: "POST", url: `/api/setups/${setup}/thumb`, headers });
  assert.deepEqual(put.json(), { thumbs: 1, voted: true });
  const again = await app.inject({ method: "GET", url: `/api/players/${slug}/setups`, headers });
  assert.equal(again.json().board.current.voted, true);
  const stranger = await app.inject({ method: "GET", url: `/api/players/${slug}/setups`, headers: { "x-voter": voterB } });
  assert.equal(stranger.json().board.current.voted, false);
  assert.equal(stranger.json().board.current.thumbs, 1);
  const nothing = await app.inject({ method: "GET", url: `/api/players/${slug}/setups` });
  assert.equal(nothing.json().board.current.voted, false);

  // the content of an earlier state
  upsertProfile(id, profileSchema.parse({ ...own, daevanion: [{ board: 10, nodes: [1, 2, 3, 4] }] }));
  const earlier = await app.inject({ method: "GET", url: `/api/setups/${setup}` });
  assert.equal(earlier.statusCode, 200);
  assert.equal(earlier.json().kind, "board");

  const gone = await app.inject({ method: "DELETE", url: `/api/setups/${setup}/thumb`, headers });
  assert.deepEqual(gone.json(), { thumbs: 0, voted: false });
  assert.equal((await app.inject({ method: "GET", url: `/api/setups/${setup}` })).statusCode, 404, "an earlier state without thumbs is gone");
  assert.equal((await app.inject({ method: "GET", url: "/api/players/nobody-here/setups" })).statusCode, 404);
  assert.equal(db.select().from(setupVotes).where(eq(setupVotes.setupId, setup)).all().length, 0);
});
