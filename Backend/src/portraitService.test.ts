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

const { pickCharacter, searchCharacter, downloadPortrait, fetchPortrait, isFresh, PortraitQueue, portraitUrlFor, RETRY_AFTER, CHARACTER_RETRY_AFTER, HOUR, DAY, fetchPlayer, pacedFetch, processPlayer, decodeCharacterId } = await import("./portraitService.js");
const { playerPortraits, playerNcCharacter, players, servers } = await import("./db/schema.js");
const { europeServerOf } = await import("./content/aion2Servers.js");
const { eq } = await import("drizzle-orm");
const jpeg = (await import("jpeg-js")).default;

const hit = (name: string, serverId: number, charKey: string, race = 1) => ({
  characterId: "AbCd1234%3D", name: `<strong>${name}</strong>`, race, serverId, profileImageUrl: `/game_profile_images/aion2global/images?gameServerKey=${serverId}&charKey=${charKey}`,
});
const key = { name: "Aahz", faction: "Elyos" as const, ownServerId: 1304 };

test("exact match wins over partial matches, tags are removed", () => {
  const list = [hit("Aahzman", 1304, "1"), hit("Aahz", 1304, "2"), hit("aahz", 1304, "3")];
  assert.deepEqual(pickCharacter(list, key), { charKey: "2", serverId: 1304, characterId: "AbCd1234=" });
});

test("several exact matches: own server settles it, otherwise none", () => {
  const list = [hit("Aahz", 1301, "1"), hit("Aahz", 1304, "2")];
  assert.deepEqual(pickCharacter(list, key), { charKey: "2", serverId: 1304, characterId: "AbCd1234=" });
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
  assert.deepEqual(await searchCharacter(fake, key), { charKey: "42", serverId: 1304, characterId: "AbCd1234=" });
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
  assert.equal((await downloadPortrait(ok, { charKey: "1", serverId: 1304, characterId: "AbCd1234=" })).length, 3);

  const evil = (async () => new Response(null, { status: 302, headers: { location: "https://evil.example.com/a.jpg" } })) as unknown as typeof fetch;
  await assert.rejects(downloadPortrait(evil, { charKey: "1", serverId: 1304, characterId: "AbCd1234=" }), /host not allowed/);

  const html = (async () => new Response("<html>", { status: 200, headers: { "content-type": "text/html" } })) as unknown as typeof fetch;
  await assert.rejects(downloadPortrait(html, { charKey: "1", serverId: 1304, characterId: "AbCd1234=" }), /not an image/);

  const huge = (async () => new Response(new Uint8Array(3 * 1024 * 1024), { status: 200, headers: { "content-type": "image/jpeg" } })) as unknown as typeof fetch;
  await assert.rejects(downloadPortrait(huge, { charKey: "1", serverId: 1304, characterId: "AbCd1234=" }), /too large/);
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

  // The character data is fresh in this test, so only the portrait's own times decide (the character data has its own test below).
  db.insert(playerNcCharacter).values({ playerId: player.id, status: "ok", fetchedAt: 1000, nextTryAt: 1e12 }).run();
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

  // Portrait fresh but character data due: queued as well.
  db.update(playerPortraits).set({ nextTryAt: 1e12 }).run();
  db.update(playerNcCharacter).set({ nextTryAt: 5000 }).run();
  portraitUrlFor(pk, 6000, fakeQueue);
  assert.equal(queued.length, 4, "due character data queues the player");
});

test("europeServerOf maps names to NC ids", () => {
  assert.deepEqual(europeServerOf("Europe - Kaisinel"), { id: 1304, faction: "Elyos" });
  assert.deepEqual(europeServerOf("Europe - Lumiel"), { id: 2304, faction: "Asmodian" });
  assert.equal(europeServerOf("Asia - Ariel"), null);
  assert.equal(europeServerOf(null), null);
});

// --- character data from the official character page ---

const ncInfo = { stat: { statList: [{ type: "STR", value: 10 }, { type: "Justice", value: 50 }, { type: "ItemLevel", value: 2146 }] } };
const ncEquipment = { petwing: { pet: { id: 1124, level: 3 }, wing: { id: 30500200 }, wingSkin: { id: 30400400 } } };
/** A fake NC: search hit, character pages, portrait image; records every request URL. */
function fakeNc(opts: { list?: unknown[]; failPage?: string; badInfo?: boolean } = {}) {
  const urls: URL[] = [];
  const jpg = jpeg.encode({ width: 64, height: 64, data: Buffer.alloc(64 * 64 * 4, 255) }, 90).data;
  const fn = (async (url: URL) => {
    urls.push(url);
    if (url.hostname.startsWith("api-search")) {
      return new Response(JSON.stringify({ list: opts.list ?? [hit("Aahz", 1304, "42")] }), { status: 200 });
    }
    if (url.hostname === "aion2.plaync.com") {
      if (opts.failPage && url.pathname.endsWith(opts.failPage)) {
        return new Response("no", { status: 500 });
      }
      return new Response(JSON.stringify(url.pathname.endsWith("/info") ? (opts.badInfo ? {} : ncInfo) : ncEquipment), { status: 200 });
    }
    return new Response(new Uint8Array(jpg), { status: 200, headers: { "content-type": "image/jpeg" } });
  }) as unknown as typeof fetch;
  return { fn, urls };
}

test("character id is decoded from the search hit and validated", () => {
  assert.equal(decodeCharacterId("AbCd1234%3D"), "AbCd1234=");
  assert.equal(decodeCharacterId("AbCd-_12"), "AbCd-_12");
  assert.equal(decodeCharacterId("a/b"), null);
  assert.equal(decodeCharacterId("%E0%A4%A"), null);
  assert.equal(decodeCharacterId(42), null);
  assert.equal(decodeCharacterId("x".repeat(400)), null);
});

test("fetchPlayer: search, image, info and equipment with the NC server id of the hit", async () => {
  const nc = fakeNc({ list: [hit("Aahz", 2304, "42", 1)] });
  const out = await fetchPlayer(nc.fn, { playerId: 1, name: "Aahz", faction: "Elyos", ownServerId: 1304 }, { portrait: true, character: true }, null);
  assert.equal(out.portrait?.status, "ok");
  assert.equal(out.character?.status, "ok");
  assert.equal(out.character?.data?.activePetLevel, 3);
  const info = nc.urls.find((u) => u.pathname === "/api/character/info")!;
  assert.equal(info.searchParams.get("characterId"), "AbCd1234=", "decoded once, encoded by the URL");
  assert.match(info.href, /characterId=AbCd1234%3D/);
  assert.equal(info.searchParams.get("serverId"), "2304", "NC's server of the hit, not the uploader's");
  assert.equal(info.searchParams.get("region"), "eu");
  assert.deepEqual(nc.urls.map((u) => u.hostname), ["api-search.plaync.com", "profileimg.plaync.com", "aion2.plaync.com", "aion2.plaync.com"]);
});

test("fetchPlayer: known character id means no search; nothing due means no request", async () => {
  const nc = fakeNc();
  const known = { characterId: "AbCd1234=", serverId: 1304 };
  const out = await fetchPlayer(nc.fn, { playerId: 1, name: "Aahz", faction: "Elyos", ownServerId: 1304 }, { portrait: false, character: true }, known);
  assert.equal(out.portrait, null);
  assert.equal(out.character?.status, "ok");
  assert.deepEqual(nc.urls.map((u) => u.pathname), ["/api/character/info", "/api/character/equipment"]);
});

test("fetchPlayer: no hit -> none; search or page failure -> error; unusable info -> error", async () => {
  const k = { playerId: 1, name: "Aahz", faction: "Elyos" as const, ownServerId: 1304 };
  const both = { portrait: true, character: true };
  const none = await fetchPlayer(fakeNc({ list: [] }).fn, k, both, null);
  assert.deepEqual([none.portrait?.status, none.character?.status], ["none", "none"]);
  const down = await fetchPlayer((async () => new Response("x", { status: 503 })) as unknown as typeof fetch, k, both, null);
  assert.deepEqual([down.portrait?.status, down.character?.status], ["error", "error"]);
  const page = await fetchPlayer(fakeNc({ failPage: "/equipment" }).fn, k, both, null);
  assert.deepEqual([page.portrait?.status, page.character?.status], ["ok", "error"], "the portrait is independent of the character pages");
  assert.equal((await fetchPlayer(fakeNc({ badInfo: true }).fn, k, both, null)).character?.status, "error");
});

test("pacedFetch keeps the pause between all requests", async () => {
  let clock = 1000;
  const sleeps: number[] = [];
  const starts: number[] = [];
  const f = pacedFetch((async () => { starts.push(clock); return new Response("{}"); }) as unknown as typeof fetch, 2000, () => clock, async (ms) => { sleeps.push(ms); clock += ms; });
  await f("https://aion2.plaync.com/a");
  await f("https://aion2.plaync.com/b");
  clock += 500;
  await f("https://aion2.plaync.com/c");
  clock += 5000;
  await f("https://aion2.plaync.com/d");
  assert.deepEqual(sleeps, [2000, 1500]);
  assert.deepEqual(starts, [1000, 3000, 5000, 10000]);
});

test("processPlayer: stores the data, keeps it on later errors, refreshes after 3 days, retries errors after an hour", async () => {
  const server = db.insert(servers).values({ fingerprint: "aion2:europe-kaisinel-nc", displayName: "Europe - Kaisinel" }).returning().get();
  const player = db.insert(players).values({ name: "Aahz", nameNormalized: "aahz-nc", serverId: server.id }).returning().get();
  const pk = { playerId: player.id, name: "Aahz", faction: "Elyos" as const, ownServerId: 1304 };
  const row = () => db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, player.id)).get()!;

  const first = fakeNc();
  await processPlayer(pk, first.fn, 1_000_000);
  assert.equal(row().status, "ok");
  assert.deepEqual(JSON.parse(row().attributesJson), { 1: 10, 7: 50 });
  assert.deepEqual([row().wingId, row().wingSkinId, row().activePet, row().activePetLevel], [30500200, 30400400, 1124, 3]);
  assert.equal(row().characterId, "AbCd1234=");
  assert.equal(row().nextTryAt, 1_000_000 + 3 * DAY);
  assert.equal(CHARACTER_RETRY_AFTER.error, HOUR);

  const idle = fakeNc();
  await processPlayer(pk, idle.fn, 1_000_000 + DAY);
  assert.equal(idle.urls.length, 0, "everything fresh: no request");

  // The portrait (7 days) is still fresh, the character data (3 days) is due: no search, no image, only the two pages.
  const refresh = fakeNc({ failPage: "/equipment" });
  await processPlayer(pk, refresh.fn, 1_000_000 + 3 * DAY + 1);
  assert.deepEqual(refresh.urls.map((u) => u.pathname), ["/api/character/info", "/api/character/equipment"]);
  assert.equal(row().status, "error");
  assert.deepEqual(JSON.parse(row().attributesJson), { 1: 10, 7: 50 }, "old data stays");
  assert.equal(row().fetchedAt, 1_000_000, "time of the last good data");
  assert.equal(row().nextTryAt, 1_000_000 + 3 * DAY + 1 + HOUR);

  const stillWaiting = fakeNc();
  await processPlayer(pk, stillWaiting.fn, 1_000_000 + 3 * DAY + 1 + HOUR - 1);
  assert.equal(stillWaiting.urls.length, 0);
  await processPlayer(pk, fakeNc().fn, 1_000_000 + 3 * DAY + 2 + HOUR);
  assert.equal(row().status, "ok");
  assert.equal(row().fetchedAt, 1_000_000 + 3 * DAY + 2 + HOUR);
});

test("processPlayer: no hit stores 'none' with a 2 day wait and no data", async () => {
  const server = db.insert(servers).values({ fingerprint: "aion2:europe-kaisinel-none", displayName: "Europe - Kaisinel" }).returning().get();
  const player = db.insert(players).values({ name: "Ghost", nameNormalized: "ghost-nc", serverId: server.id }).returning().get();
  await processPlayer({ playerId: player.id, name: "Ghost", faction: "Elyos", ownServerId: 1304 }, fakeNc({ list: [] }).fn, 5000);
  const r = db.select().from(playerNcCharacter).where(eq(playerNcCharacter.playerId, player.id)).get()!;
  assert.deepEqual([r.status, r.attributesJson, r.nextTryAt], ["none", "{}", 5000 + 2 * DAY]);
});
