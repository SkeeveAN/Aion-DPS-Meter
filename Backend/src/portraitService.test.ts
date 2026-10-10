import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-portrait-test-")), "test.sqlite");
const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { pickCharacter, searchCharacter, downloadPortrait, fetchPortrait, isFresh, PortraitQueue, portraitUrlFor, RETRY_AFTER, HOUR, DAY } = await import("./portraitService.js");
const { playerPortraits, players, servers } = await import("./db/schema.js");
const { europeServerOf } = await import("./content/aion2Servers.js");
const jpeg = (await import("jpeg-js")).default;

const hit = (name: string, serverId: number, charKey: string, race = 1) => ({
  characterId: "x%3D", name: `<strong>${name}</strong>`, race, serverId, profileImageUrl: `/game_profile_images/aion2global/images?gameServerKey=${serverId}&charKey=${charKey}`,
});
const key = { name: "Aahz", faction: "Elyos" as const, ownServerId: 1304 };

test("exact match wins over partial matches, tags are removed", () => {
  const list = [hit("Aahzman", 1304, "1"), hit("Aahz", 1304, "2"), hit("aahz", 1304, "3")];
  assert.deepEqual(pickCharacter(list, key), { charKey: "2", serverId: 1304 });
});

test("several exact matches: own server settles it, otherwise none", () => {
  const list = [hit("Aahz", 1301, "1"), hit("Aahz", 1304, "2")];
  assert.deepEqual(pickCharacter(list, key), { charKey: "2", serverId: 1304 });
  assert.equal(pickCharacter(list, { ...key, ownServerId: null }), null);
  assert.equal(pickCharacter([hit("Aahz", 1301, "1"), hit("Aahz", 1302, "2")], key), null);
});

test("no hit, wrong race, bad data: none", () => {
  assert.equal(pickCharacter([], key), null);
  assert.equal(pickCharacter([hit("Aahz", 2304, "1", 2)], key), null);
  assert.equal(pickCharacter([{ name: "Aahz", race: 1, serverId: 1304, profileImageUrl: "/x?charKey=abc" }], key), null);
  assert.equal(pickCharacter("nope", key), null);
});

test("search asks NC with the browser headers and the name url-encoded", async () => {
  let seen: URL | undefined;
  let headers: Record<string, string> = {};
  const fake = (async (url: URL, init: RequestInit) => {
    seen = url;
    headers = init.headers as Record<string, string>;
    return new Response(JSON.stringify({ list: [hit("Aahz", 1304, "42")] }), { status: 200 });
  }) as unknown as typeof fetch;
  assert.deepEqual(await searchCharacter(fake, key), { charKey: "42", serverId: 1304 });
  assert.equal(seen!.hostname, "api-search.plaync.com");
  assert.equal(seen!.searchParams.get("keyword"), "Aahz");
  assert.equal(seen!.searchParams.get("region"), "eu");
  assert.equal(headers.Origin, "https://aion2.plaync.com");
  assert.match(headers["User-Agent"], /Mozilla/);
});

test("image download follows redirects only to NC hosts and checks content type", async () => {
  const ok = (async (url: URL) =>
    url.hostname === "profileimg.plaync.com"
      ? new Response(null, { status: 302, headers: { location: "https://fizz-download.playnccdn.com/a.jpg" } })
      : new Response(new Uint8Array([1, 2, 3]), { status: 200, headers: { "content-type": "image/jpeg" } })) as unknown as typeof fetch;
  assert.equal((await downloadPortrait(ok, { charKey: "1", serverId: 1304 })).length, 3);

  const evil = (async () => new Response(null, { status: 302, headers: { location: "https://evil.example.com/a.jpg" } })) as unknown as typeof fetch;
  await assert.rejects(downloadPortrait(evil, { charKey: "1", serverId: 1304 }), /host not allowed/);

  const html = (async () => new Response("<html>", { status: 200, headers: { "content-type": "text/html" } })) as unknown as typeof fetch;
  await assert.rejects(downloadPortrait(html, { charKey: "1", serverId: 1304 }), /not an image/);

  const huge = (async () => new Response(new Uint8Array(3 * 1024 * 1024), { status: 200, headers: { "content-type": "image/jpeg" } })) as unknown as typeof fetch;
  await assert.rejects(downloadPortrait(huge, { charKey: "1", serverId: 1304 }), /too large/);
});

test("fetchPortrait: ok, none and error outcomes", async () => {
  const pixels = Buffer.alloc(64 * 64 * 4, 255);
  const jpg = jpeg.encode({ width: 64, height: 64, data: pixels }, 90).data;
  const route = (search: unknown, status = 200) =>
    (async (url: URL) =>
      url.hostname.startsWith("api-search")
        ? new Response(JSON.stringify(search), { status })
        : new Response(new Uint8Array(jpg), { status: 200, headers: { "content-type": "image/jpeg" } })) as unknown as typeof fetch;
  const pk = { playerId: 1, ...key };
  const ok = await fetchPortrait(route({ list: [hit("Aahz", 1304, "7")] }), pk);
  assert.equal(ok.status, "ok");
  assert.ok(ok.png && ok.png.length > 100);
  assert.equal((await fetchPortrait(route({ list: [] }), pk)).status, "none");
  assert.equal((await fetchPortrait(route({}, 500), pk)).status, "error");
});

test("retry times: ok 7 days, none 2 days, error 1 hour; freshness", () => {
  assert.equal(RETRY_AFTER.ok, 7 * DAY);
  assert.equal(RETRY_AFTER.none, 2 * DAY);
  assert.equal(RETRY_AFTER.error, HOUR);
  assert.equal(isFresh(undefined, 100), false);
  assert.equal(isFresh({ nextTryAt: 200 }, 100), true);
  assert.equal(isFresh({ nextTryAt: 100 }, 100), false);
});

test("queue: one job at a time, pause between jobs, no duplicates, bounded", async () => {
  let clock = 10_000;
  const sleeps: number[] = [];
  const order: number[] = [];
  let active = 0;
  let maxActive = 0;
  const q = new PortraitQueue(
    async (k) => {
      active++;
      maxActive = Math.max(maxActive, active);
      await Promise.resolve();
      order.push(k.playerId);
      active--;
    },
    { minGapMs: 2000, maxLength: 3, now: () => clock, sleep: async (ms) => { sleeps.push(ms); clock += ms; } },
  );
  const k = (playerId: number) => ({ playerId, name: "n", faction: null, ownServerId: null });
  assert.equal(q.enqueue(k(1)), true);
  assert.equal(q.enqueue(k(1)), false, "duplicate");
  assert.equal(q.enqueue(k(2)), true);
  assert.equal(q.enqueue(k(3)), true);
  assert.equal(q.enqueue(k(4)), false, "full");
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual(order, [1, 2, 3]);
  assert.equal(maxActive, 1);
  assert.deepEqual(sleeps, [2000, 2000]);
});

test("a failing job does not stop the queue", async () => {
  const done: number[] = [];
  const q = new PortraitQueue(async (k) => { if (k.playerId === 1) throw new Error("x"); done.push(k.playerId); }, { minGapMs: 0, maxLength: 10, now: () => 0, sleep: async () => {} });
  q.enqueue({ playerId: 1, name: "a", faction: null, ownServerId: null });
  q.enqueue({ playerId: 2, name: "b", faction: null, ownServerId: null });
  await new Promise((r) => setTimeout(r, 20));
  assert.deepEqual(done, [2]);
});

test("portraitUrlFor: queues without valid entry, URL only for status ok", async () => {
  const server = db.insert(servers).values({ fingerprint: "aion2:europe-kaisinel", displayName: "Europe - Kaisinel" }).returning().get();
  const player = db.insert(players).values({ name: "Aahz", nameNormalized: "aahz", serverId: server.id }).returning().get();
  const queued: number[] = [];
  const fakeQueue = { enqueue: (k: { playerId: number }) => { queued.push(k.playerId); return true; } } as unknown as InstanceType<typeof PortraitQueue>;
  const pk = { playerId: player.id, name: "Aahz", faction: "Elyos" as const, ownServerId: 1304 };
  assert.equal(portraitUrlFor(pk, 1000, fakeQueue), null);
  assert.deepEqual(queued, [player.id]);

  db.insert(playerPortraits).values({ playerId: player.id, status: "none", fetchedAt: 1000, nextTryAt: 5000 }).run();
  assert.equal(portraitUrlFor(pk, 2000, fakeQueue), null);
  assert.equal(queued.length, 1, "fresh 'none' is not retried");
  assert.equal(portraitUrlFor(pk, 6000, fakeQueue), null);
  assert.equal(queued.length, 2, "expired 'none' is retried");

  db.update(playerPortraits).set({ status: "ok", charKey: "1", ncServerId: 1304, fetchedAt: 1000, nextTryAt: 5000 }).run();
  assert.equal(portraitUrlFor(pk, 2000, fakeQueue), `/api/players/${player.id}/portrait?v=1000`);
  assert.equal(queued.length, 2);
  assert.equal(portraitUrlFor(pk, 6000, fakeQueue), `/api/players/${player.id}/portrait?v=1000`, "stale picture stays while refreshing");
  assert.equal(queued.length, 3);
});

test("europeServerOf maps names to NC ids", () => {
  assert.deepEqual(europeServerOf("Europe - Kaisinel"), { id: 1304, faction: "Elyos" });
  assert.deepEqual(europeServerOf("Europe - Lumiel"), { id: 2304, faction: "Asmodian" });
  assert.equal(europeServerOf("Asia - Ariel"), null);
  assert.equal(europeServerOf(null), null);
});
