import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-ncmatch-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const ncMatchModule = await import("./ncMatch.js");
const { decide, parseHits, processMatch, noteClientServer, dueOf, MatchQueue, shardOf, MATCH_RETRY_AFTER } = await import("./ncMatch.js");
const { players, servers, playerProfiles } = await import("./db/schema.js");
const { linkPlayerGuild } = await import("./guilds.js");
const { eq } = await import("drizzle-orm");

type Hit = ReturnType<typeof parseHits>[number];
const hit = (o: Partial<Hit> & { name: string }): Hit => ({ race: 1, serverId: 1304, level: 45, classId: 1, characterId: null, ...o });
const subject = (o: Partial<Parameters<typeof decide>[1]> = {}) => ({ name: "Aahz", faction: "", className: "", classFromFight: false, level: null, legionServers: [], ...o });

test("a name with one character is that player", () => {
  const d = decide([hit({ name: "Aahz" }), hit({ name: "Aahza", serverId: 1305 })], subject());
  assert.equal(d.status, "verified");
  assert.equal(d.hit?.serverId, 1304);
  assert.equal(d.via, "name");
});

test("the same name on both factions is told apart by the faction", () => {
  const hits = [hit({ name: "Aahz", race: 2, serverId: 2303 }), hit({ name: "Aahz", race: 1, serverId: 1304 })];
  assert.equal(decide(hits, subject()).status, "ambiguous");
  assert.equal(decide(hits, subject({ faction: "Elyos" })).hit?.serverId, 1304);
  assert.equal(decide(hits, subject({ faction: "Asmodian" })).hit?.serverId, 2303);
});

test("class and level narrow the candidates, a legion decides the rest", () => {
  const hits = [hit({ name: "Miko", serverId: 1304, classId: 8, level: 45 }), hit({ name: "Miko", serverId: 1305, classId: 8, level: 45 }), hit({ name: "Miko", serverId: 1306, classId: 2, level: 45 })];
  assert.equal(decide(hits, subject({ name: "Miko", className: "Chanter" })).status, "ambiguous", "two Chanters remain");
  assert.equal(decide(hits, subject({ name: "Miko", className: "Chanter", legionServers: [1305] })).hit?.serverId, 1305);
  assert.equal(decide(hits, subject({ name: "Miko", className: "Chanter", legionServers: [1305] })).via, "legion");
  const lowLevels = [hit({ name: "Kai", serverId: 1304, level: 12 }), hit({ name: "Kai", serverId: 1305, level: 45 })];
  assert.equal(decide(lowLevels, subject({ name: "Kai", level: 40 })).hit?.serverId, 1305, "a character never loses levels");
});

test("nothing is guessed: no hit, a hit of the other faction, or a lone hit of another class give no match", () => {
  assert.equal(decide([], subject()).status, "none");
  assert.equal(decide([hit({ name: "Aahz", race: 2 })], subject({ faction: "Elyos" })).status, "none");
  assert.equal(decide([hit({ name: "Aahz", classId: 8 })], subject({ className: "Gladiator", classFromFight: true })).status, "none");
  assert.equal(decide([hit({ name: "Aahz", classId: 8 })], subject({ className: "Gladiator", classFromFight: false })).status, "verified", "a class from his profile only narrows, it does not veto");
});

test("a search answer is read defensively", () => {
  assert.deepEqual(parseHits("x"), []);
  const parsed = parseHits([{ name: "<strong>Aa</strong>hz", race: 1, serverId: 1304, level: 45, pcId: 6, characterId: "abc" }, { name: "Bad", race: 3, serverId: 1304 }, { name: "Bad2", race: 1, serverId: 7 }, null]);
  assert.equal(parsed.length, 1);
  assert.equal(parsed[0].name, "Aahz");
  assert.equal(parsed[0].classId, 1, "(6 - 1) / 4 = class 1, Gladiator");
});

test("regions", () => {
  assert.equal(shardOf("Europe - Kaisinel"), "eu");
  assert.equal(shardOf("Asia - Fregion"), "as");
  assert.equal(shardOf("NA West - Siel"), "naw");
  assert.equal(shardOf("Hogalum [1317]"), null);
});

// --- processMatch against a simulated NC search ---
const server = (fingerprint: string, displayName: string) => Number(db.insert(servers).values({ fingerprint, displayName }).run().lastInsertRowid);
const kaisinel = server("aion2:europe-kaisinel", "Europe - Kaisinel");
const fregion = server("aion2:asia-fregion", "Asia - Fregion");
let n = 0;
const player = (name: string, serverId: number, extra: Partial<typeof players.$inferInsert> = {}) =>
  Number(db.insert(players).values({ name, nameNormalized: name.toLowerCase(), serverId, slug: `${name.toLowerCase()}-${++n}`, ...extra }).run().lastInsertRowid);
const serverOf = (id: number) => (sqlite.prepare("select s.display_name d, p.faction f from players p join servers s on s.id = p.server_id where p.id = ?").get(id) as { d: string; f: string });
const matchRow = (id: number) => sqlite.prepare("select status, nc_server_id s, via from player_nc_match where player_id = ?").get(id) as { status: string; s: number | null; via: string | null } | undefined;

function ncAnswer(list: object[], seen: string[] = []) {
  return (async (input: unknown) => {
    seen.push(String(input));
    return new Response(JSON.stringify({ list, pagination: { page: 1, size: 40, total: list.length, endPage: 1 } }), { status: 200, headers: { "content-type": "application/json" } });
  }) as unknown as typeof fetch;
}
const raw = (o: { name: string; race?: number; serverId: number; level?: number; pcId?: number; characterId?: string }) => ({ race: 1, level: 45, pcId: 6, ...o });

test("a single matching character moves the player to his real server and gives him his faction", async () => {
  const id = player("Wanderer", kaisinel);
  const seen: string[] = [];
  const status = await processMatch(id, ncAnswer([raw({ name: "<strong>Wanderer</strong>", race: 2, serverId: 2308 })], seen), 1_000_000);
  assert.equal(status, "verified");
  assert.deepEqual(serverOf(id), { d: "Europe - Beritra", f: "Asmodian" });
  assert.equal(matchRow(id)?.status, "verified");
  assert.match(seen[0], /region=eu/);
});

test("the region of the player's own server is searched", async () => {
  const id = player("Fernost", fregion);
  const seen: string[] = [];
  await processMatch(id, ncAnswer([raw({ name: "Fernost", serverId: 1507 })], seen), 1_000_000);
  assert.match(seen[0], /region=as/);
  assert.equal(serverOf(id).d, "Asia - Fregion");
});

test("a match that would put him on a server where his name is already taken changes nothing", async () => {
  const beritra = Number((sqlite.prepare("select id from servers where display_name = 'Europe - Beritra'").get() as { id: number }).id);
  player("Doppelt", beritra);
  const id = player("Doppelt", kaisinel);
  const status = await processMatch(id, ncAnswer([raw({ name: "Doppelt", serverId: 2308 })]), 1_000_000);
  assert.equal(status, "conflict");
  assert.equal(serverOf(id).d, "Europe - Kaisinel");
});

test("several fitting characters stay unclear and are asked again later, not now", async () => {
  const id = player("Haeufig", kaisinel);
  const status = await processMatch(id, ncAnswer([raw({ name: "Haeufig", serverId: 1304 }), raw({ name: "Haeufig", serverId: 1305 })]), 1_000_000);
  assert.equal(status, "ambiguous");
  assert.equal(serverOf(id).d, "Europe - Kaisinel");
  assert.deepEqual(dueOf([id], 1_000_000 + MATCH_RETRY_AFTER.ambiguous - 1), []);
  assert.deepEqual(dueOf([id], 1_000_000 + MATCH_RETRY_AFTER.ambiguous + 1), [id]);
});

test("a network problem is recorded and retried in an hour; nothing is changed", async () => {
  const id = player("Offline", kaisinel);
  const status = await processMatch(id, (async () => { throw new Error("down"); }) as unknown as typeof fetch, 1_000_000);
  assert.equal(status, "error");
  assert.equal(serverOf(id).d, "Europe - Kaisinel");
  assert.deepEqual(dueOf([id], 1_000_000 + MATCH_RETRY_AFTER.error + 1), [id]);
});

test("the verified members of a legion settle a name that has several characters", async () => {
  const mates = [player("Mate1", kaisinel, { guild: "Eisenfaust" }), player("Mate2", kaisinel, { guild: "Eisenfaust" })];
  const member = player("Gleichname", kaisinel, { guild: "Eisenfaust" });
  for (const id of [...mates, member]) linkPlayerGuild(id);
  for (const id of mates) noteClientServer(id, 1305, 1_000_000);
  const status = await processMatch(member, ncAnswer([raw({ name: "Gleichname", serverId: 1304 }), raw({ name: "Gleichname", serverId: 1305 })]), 1_000_000);
  assert.equal(status, "verified");
  assert.equal(matchRow(member)?.via, "legion");
  assert.equal(serverOf(member).d, "Europe - Yustiel", "1305 is Yustiel");
});

test("a player whose own client named his server is checked by NC too, but never moved and never demoted", async () => {
  const id = player("Selbst", kaisinel);
  noteClientServer(id, 1304, 1_000_000);
  assert.deepEqual(dueOf([id], 1_000_000), [id], "due at once");
  noteClientServer(id, 1304, 1_000_500);
  assert.equal((sqlite.prepare("select checked_at c from player_nc_match where player_id = ?").get(id) as { c: number }).c, 1_000_000, "a later upload does not reset the row");
  // NC lists a namesake on another server only: his client's word stays
  assert.equal(await processMatch(id, ncAnswer([raw({ name: "Selbst", serverId: 1305 })]), 2_000_000), "client");
  assert.equal(matchRow(id)?.status, "client");
  assert.equal(matchRow(id)?.s, 1304);
  // NC may simply not list a young character yet: asked again the next day, not in a month
  assert.deepEqual(dueOf([id], 2_000_000 + 23 * 3_600_000), []);
  assert.deepEqual(dueOf([id], 2_000_000 + 25 * 3_600_000), [id]);
  // NC knows him on that server: verified
  assert.equal(await processMatch(id, ncAnswer([raw({ name: "Selbst", serverId: 1305 }), raw({ name: "Selbst", serverId: 1304 })]), 3_000_000), "verified");
  assert.equal(matchRow(id)?.status, "verified");
});

test("a player NC does not know on the server his client named is moved to the one character of his faction and class elsewhere", async () => {
  // SaintlyOrder 2026-10-17: the party roster printed 2306 in front of his name and 2303 behind it; NC lists him on 2303 only
  const id = player("Falschserver", kaisinel);
  db.insert(playerProfiles).values({ playerId: id, source: "seen", classId: 8, level: 45 }).run();
  noteClientServer(id, 1304, 1_000_000);
  assert.equal(await processMatch(id, ncAnswer([raw({ name: "Falschserver", serverId: 1305, pcId: 34 })]), 2_000_000), "verified");
  assert.equal(matchRow(id)?.status, "verified");
  assert.equal(matchRow(id)?.s, 1305);
  assert.equal(matchRow(id)?.via, "name-elsewhere");
  assert.equal(serverOf(id).d, "Europe - Yustiel", "1305 is Yustiel");
});

test("a namesake of another class, or one without a known class, never moves a player whose client named his server", async () => {
  const other = player("Namensvetter", kaisinel);
  db.insert(playerProfiles).values({ playerId: other, source: "seen", classId: 8, level: 45 }).run();
  noteClientServer(other, 1304, 1_000_000);
  assert.equal(await processMatch(other, ncAnswer([raw({ name: "Namensvetter", serverId: 1305, pcId: 6 })]), 2_000_000), "client", "a Gladiator is not the Chanter he is");
  assert.equal(serverOf(other).d, "Europe - Kaisinel");
  const unknown = player("Klasselos", kaisinel);
  noteClientServer(unknown, 1304, 1_000_000);
  assert.equal(await processMatch(unknown, ncAnswer([raw({ name: "Klasselos", serverId: 1305, pcId: 34 })]), 2_000_000), "client");
  assert.equal(serverOf(unknown).d, "Europe - Kaisinel");
});

test("a profile's class helps to pick, and a level lower than his own is no match", async () => {
  const id = player("Stufig", kaisinel);
  db.insert(playerProfiles).values({ playerId: id, source: "seen", classId: 8, level: 40 }).run();
  const status = await processMatch(id, ncAnswer([raw({ name: "Stufig", serverId: 1304, pcId: 34, level: 12 }), raw({ name: "Stufig", serverId: 1305, pcId: 34, level: 45 })]), 1_000_000);
  assert.equal(status, "verified");
  assert.equal(serverOf(id).d, "Europe - Yustiel");
});

test("the queue works one player at a time and takes each only once", async () => {
  const order: number[] = [];
  let active = 0;
  let overlap = false;
  const q = new MatchQueue(async (id) => {
    active++;
    overlap ||= active > 1;
    await new Promise((r) => setTimeout(r, 2));
    order.push(id);
    active--;
  });
  assert.equal(q.enqueue([1, 2, 2, 3]), 3);
  await q.idle();
  assert.deepEqual(order, [1, 2, 3]);
  assert.equal(overlap, false);
});

test("a player without a legion or region we can search is not due", () => {
  const unknown = server("aion2:aion-2-server-9999", "Hogalum [9999]");
  const id = player("Fremd", unknown);
  assert.deepEqual(dueOf([id]), []);
  assert.deepEqual(db.select().from(players).where(eq(players.id, id)).all().length, 1);
});

test("a confirmed player gets a fetch key with his real server and race, an unconfirmed one does not; the sweep lists those without data", () => {
  const { enrichKeyOf, enrichDue } = ncMatchModule;
  const id = player("Bestaetigt", kaisinel);
  assert.equal(enrichKeyOf(id), null, "not checked yet");
  noteClientServer(id, 1304, 1_000_000);
  assert.equal(enrichKeyOf(id), null, "his own client's word alone is not enough, NC must confirm him");
  sqlite.prepare("update player_nc_match set status = 'verified', nc_race = 2 where player_id = ?").run(id);
  assert.deepEqual(enrichKeyOf(id), { playerId: id, name: "Bestaetigt", faction: "Asmodian", ownServerId: 1304, region: "eu" });
  assert.ok(enrichDue(1000).includes(id), "no portrait and no character data yet");
  sqlite.prepare("insert into player_portraits (player_id, status, fetched_at, next_try_at) values (?, 'ok', 1, ?)").run(id, Date.now() + 1e9);
  sqlite.prepare("insert into player_nc_character (player_id, status, fetched_at, next_try_at) values (?, 'ok', 1, ?)").run(id, Date.now() + 1e9);
  assert.ok(!enrichDue(1000).includes(id), "fresh data, nothing to do");
});

test("a confirmed player of another region gets a key with that region and is listed by the sweep", () => {
  const { enrichKeyOf, enrichDue } = ncMatchModule;
  const id = player("Nordamerika", server("aion2:na-east-nezekan", "NA East - Nezekan"));
  sqlite.prepare("insert into player_nc_match (player_id, status, nc_server_id, nc_race, checked_at, next_try_at) values (?, 'verified', 1102, 1, 1, 1)").run(id);
  assert.deepEqual(enrichKeyOf(id), { playerId: id, name: "Nordamerika", faction: "Elyos", ownServerId: 1102, region: "nae" });
  assert.ok(enrichDue(1000).includes(id));
});

test("the id NC gives the matched character is kept (decoded) with the match", async () => {
  const id = player("Idhaber", kaisinel);
  assert.equal(await processMatch(id, ncAnswer([raw({ name: "Idhaber", serverId: 1304, characterId: "abc_-xyz%3D" })]), 1_000_000), "verified");
  const row = sqlite.prepare("select nc_character_id c from player_nc_match where player_id = ?").get(id) as { c: string | null };
  assert.equal(row.c, "abc_-xyz=");
});

// A confirmed player is asked for by his id: NC answers the character info with his current name.
const infoAnswer = (name: string | null, seen: string[] = []) =>
  (async (input: unknown) => {
    seen.push(String(input));
    return new Response(JSON.stringify({ profile: { characterName: name, regionName: "Legion", characterLevel: 50 } }), { status: 200, headers: { "content-type": "application/json" } });
  }) as unknown as typeof fetch;
const confirmed = (name: string, characterId: string) => {
  const id = player(name, kaisinel, { faction: "Elyos" });
  sqlite.prepare("insert into player_nc_match (player_id, status, nc_server_id, nc_race, nc_character_id, candidates, checked_at, next_try_at) values (?, 'verified', 1304, 1, ?, 1, 1, 1)").run(id, characterId);
  return id;
};
const nameOf = (id: number) => sqlite.prepare("select name, name_normalized n, alias_names_normalized a, slug from players where id = ?").get(id) as { name: string; n: string; a: string; slug: string };

test("a confirmed player is looked up by his character id and renamed in place when NC names him differently", async () => {
  const id = confirmed("Altname", "idRename1");
  const before = nameOf(id);
  const seen: string[] = [];
  assert.equal(await processMatch(id, infoAnswer("Neuname", seen), 2_000_000), "verified");
  assert.match(seen[0], /characterId=idRename1/);
  assert.equal(seen.length, 1, "no search by the old name");
  const after = nameOf(id);
  assert.equal(after.name, "Neuname");
  assert.equal(after.n, "neuname");
  assert.deepEqual(JSON.parse(after.a), ["altname"], "the old name stays findable");
  assert.equal(after.slug, before.slug, "links must not rot");
  assert.equal(matchRow(id)?.via, "renamed");
});

test("a new name that another player of the server already carries is left for a human, nothing is merged", async () => {
  const id = confirmed("Vorher", "idRename2");
  const other = player("Nachher", kaisinel);
  assert.equal(await processMatch(id, infoAnswer("Nachher"), 2_000_000), "verified");
  assert.equal(nameOf(id).name, "Vorher");
  assert.equal(nameOf(other).name, "Nachher");
  assert.equal(matchRow(id)?.via, "rename-conflict");
});

test("an unchanged name only fixes the spelling, and an id NC no longer knows falls back to the name search", async () => {
  const id = confirmed("gleich", "idRename3");
  assert.equal(await processMatch(id, infoAnswer("Gleich"), 2_000_000), "verified");
  assert.equal(nameOf(id).name, "Gleich");
  const gone = confirmed("Verschwunden", "idRename4");
  const seen: string[] = [];
  const answer = (async (input: unknown) => {
    seen.push(String(input));
    return String(input).includes("/character/info")
      ? new Response(JSON.stringify({ profile: { characterName: null } }), { status: 200 })
      : new Response(JSON.stringify({ list: [], pagination: { endPage: 1 } }), { status: 200 });
  }) as unknown as typeof fetch;
  assert.equal(await processMatch(gone, answer, 2_000_000), "none");
  assert.equal(seen.length, 2);
});

test("an ambiguous name is settled by the legion NC's character page names for each candidate", async () => {
  const id = player("Zwilling", kaisinel, { guild: "Nocturne" });
  const seen: string[] = [];
  const answer = (async (input: unknown) => {
    const url = String(input);
    seen.push(url);
    if (url.includes("/character/info")) {
      const guild = url.includes("charA") ? "Andere" : "nocturne";
      return new Response(JSON.stringify({ profile: { characterName: "Zwilling", regionName: guild, characterLevel: 50 } }), { status: 200 });
    }
    return new Response(JSON.stringify({ list: [raw({ name: "Zwilling", serverId: 1304, characterId: "charAxyz" }), raw({ name: "Zwilling", serverId: 1305, characterId: "charBxyz" })], pagination: { endPage: 1 } }), { status: 200 });
  }) as unknown as typeof fetch;
  assert.equal(await processMatch(id, answer, 1_000_000), "verified");
  assert.equal(matchRow(id)?.via, "legion-name");
  assert.equal(serverOf(id).d, "Europe - Yustiel", "1305 is Yustiel, where the legion matches");
  assert.ok(seen.some((u) => u.includes("charAxyz")) && seen.some((u) => u.includes("charBxyz")), "every candidate was asked");
});

test("candidates in the same legion, or a player without a legion, stay ambiguous", async () => {
  const same = player("Gleichlegion", kaisinel, { guild: "Nocturne" });
  const nolegion = player("Ohnelegion", kaisinel);
  const answer = (name: string) => (async (input: unknown) => {
    const url = String(input);
    if (url.includes("/character/info")) {
      return new Response(JSON.stringify({ profile: { characterName: name, regionName: "Nocturne", characterLevel: 50 } }), { status: 200 });
    }
    return new Response(JSON.stringify({ list: [raw({ name, serverId: 1304, characterId: "idOne1" }), raw({ name, serverId: 1305, characterId: "idTwo2" })], pagination: { endPage: 1 } }), { status: 200 });
  }) as unknown as typeof fetch;
  assert.equal(await processMatch(same, answer("Gleichlegion"), 1_000_000), "ambiguous");
  assert.equal(await processMatch(nolegion, answer("Ohnelegion"), 1_000_000), "ambiguous");
});

test("an upload sends the confirmed players it names to the front of the queue when their last look is old, a level above NC's page, or missing", () => {
  const { refreshAfterUpload, enrichKeyOf } = ncMatchModule;
  const queued: { id: number; priority: number; force: boolean }[] = [];
  const fakeQueue = { enqueue: (key: { playerId: number }, how: { priority: number; force: boolean }) => { queued.push({ id: key.playerId, ...how }); return true; } } as unknown as Parameters<typeof refreshAfterUpload>[1];
  const noMatches = { enqueue: () => 0 } as unknown as Parameters<typeof refreshAfterUpload>[2];
  const fresh = confirmed("Frisch", "idUp1");
  const old = confirmed("Alt", "idUp2");
  const levelled = confirmed("Aufgestiegen", "idUp3");
  const unknown = player("Unbestaetigt", kaisinel);
  assert.ok(enrichKeyOf(fresh));
  const now = 10 * 24 * 3_600_000;
  const row = (id: number, fetchedAt: number, level: number) => sqlite.prepare("insert into player_nc_character (player_id, status, fetched_at, next_try_at, nc_level) values (?, 'ok', ?, ?, ?)").run(id, fetchedAt, now + 1e9, level);
  row(fresh, now - 3_600_000, 45);
  row(old, now - 13 * 3_600_000, 45);
  row(levelled, now - 3_600_000, 44);
  assert.equal(refreshAfterUpload([{ id: fresh }, { id: old }, { id: levelled, level: 45 }, { id: unknown }], fakeQueue, noMatches, now), 2);
  assert.deepEqual(queued, [{ id: old, priority: 1, force: true }, { id: levelled, priority: 1, force: true }]);
  assert.equal(refreshAfterUpload([{ id: fresh, level: 45 }], fakeQueue, noMatches, now), 0, "a look an hour ago and the same level: nothing to do");
});

test("an upload that shows another class than NC's page sends the player to the match check, nothing else", () => {
  const { refreshAfterUpload } = ncMatchModule;
  const id = confirmed("Klassenwechsel", "idCls0");
  sqlite.prepare("insert into player_nc_character (player_id, status, fetched_at, next_try_at, nc_class_id) values (?, 'ok', ?, ?, 1)").run(id, 1, 1e12);
  const checked: number[][] = [];
  const queued: number[] = [];
  const matches = { enqueue: (ids: number[]) => { checked.push(ids); return ids.length; } } as unknown as Parameters<typeof refreshAfterUpload>[2];
  const portraits = { enqueue: (k: { playerId: number }) => { queued.push(k.playerId); return true; } } as unknown as Parameters<typeof refreshAfterUpload>[1];
  assert.equal(refreshAfterUpload([{ id, classId: 1 }], portraits, matches, 2e12), 1, "the same class: only the old look is refreshed");
  assert.deepEqual(checked, []);
  refreshAfterUpload([{ id, classId: 4 }], portraits, matches, 2e12);
  assert.deepEqual(checked, [[id]]);
  assert.deepEqual(queued, [id], "only the first call queued a fetch");
});

test("a confirmed id whose class does not fit what the game shows is not him: with no other candidate his NC data goes, with a namesake of the right class the match moves to that one", async () => {
  const idOnly = confirmed("Falsche", "idKlasse1");
  sqlite.prepare("insert into player_profiles (player_id, source, class_id) values (?, 'seen', 4)").run(idOnly);
  sqlite.prepare("insert into player_nc_character (player_id, status, fetched_at, next_try_at) values (?, 'ok', 1, 1)").run(idOnly);
  const answer = (candidates: object[]) => (async (input: unknown) => {
    const url = String(input);
    if (url.includes("/character/info")) {
      return new Response(JSON.stringify({ profile: { characterName: "x", regionName: "L", characterLevel: 50, pcId: 6 } }), { status: 200 }); // pcId 6 = class 1, a Gladiator
    }
    return new Response(JSON.stringify({ list: candidates, pagination: { endPage: 1 } }), { status: 200 });
  }) as unknown as typeof fetch;
  // The search lists the same id only: it is disproved, nothing is left.
  assert.equal(await processMatch(idOnly, answer([raw({ name: "Falsche", serverId: 1304, characterId: "idKlasse1", pcId: 6 })]), 2_000_000), "none");
  assert.equal((sqlite.prepare("select count(*) c from player_nc_character where player_id = ?").get(idOnly) as { c: number }).c, 0, "his NC data is gone");

  const idTwo = confirmed("Wechsler", "idKlasse2");
  sqlite.prepare("insert into player_profiles (player_id, source, class_id) values (?, 'seen', 4)").run(idTwo);
  sqlite.prepare("insert into player_nc_character (player_id, status, fetched_at, next_try_at) values (?, 'ok', 1, 1)").run(idTwo);
  const status = await processMatch(idTwo, answer([raw({ name: "Wechsler", serverId: 1304, characterId: "idKlasse2", pcId: 6 }), raw({ name: "Wechsler", serverId: 1305, characterId: "idKlasse3", pcId: 14 })]), 2_000_000);
  assert.equal(status, "verified");
  assert.equal((sqlite.prepare("select nc_character_id c from player_nc_match where player_id = ?").get(idTwo) as { c: string }).c, "idKlasse3");
  assert.equal((sqlite.prepare("select count(*) c from player_nc_character where player_id = ?").get(idTwo) as { c: number }).c, 0, "the old character's data is gone");
});

// --- gear score: the last tie-breaker when name, class, level and legion leave several characters ---
function gearAnswer(name: string, levels: Record<string, number | null>) {
  return (async (input: unknown) => {
    const url = String(input);
    if (url.includes("/character/info")) {
      const key = Object.keys(levels).find((k) => url.includes(k)) as string;
      const stats = [{ name: "STR", type: "STR", value: 10, statSecondList: [] }, ...(levels[key] === null ? [] : [{ name: "ItemLevel", type: "ItemLevel", value: levels[key], statSecondList: [] }])];
      return new Response(JSON.stringify({ profile: { characterName: name, regionName: "", characterLevel: 45 }, stat: { statList: stats } }), { status: 200 });
    }
    return new Response(JSON.stringify({ list: Object.keys(levels).map((k, i) => raw({ name, serverId: 1304 + i, characterId: k })), pagination: { endPage: 1 } }), { status: 200 });
  }) as unknown as typeof fetch;
}
const profileOf = (id: number, gearScore: number | null, source: "seen" | "nc" = "seen") =>
  db.insert(playerProfiles).values({ playerId: id, source, updatedAt: "2026-10-12 00:00:00", gearScore }).run();

test("the gear score the game showed picks the one candidate whose item level fits", async () => {
  const id = player("Gearzwilling", kaisinel);
  profileOf(id, 1500);
  assert.equal(await processMatch(id, gearAnswer("Gearzwilling", { charAaaa1: 1520, charBbbb2: 2300 }), 1_000_000), "verified");
  assert.equal(matchRow(id)?.via, "gear");
  assert.equal(matchRow(id)?.s, 1304);
});

test("gear stays open when two candidates fit, one is in the grey zone, one has no item level, or the gear score is missing or came from NC", async () => {
  const both = player("Gearbeide", kaisinel);
  profileOf(both, 1500);
  assert.equal(await processMatch(both, gearAnswer("Gearbeide", { charAaaa1: 1510, charBbbb2: 1530 }), 1_000_000), "ambiguous");
  const grey = player("Geargrau", kaisinel);
  profileOf(grey, 1500);
  assert.equal(await processMatch(grey, gearAnswer("Geargrau", { charAaaa1: 1520, charBbbb2: 1380 }), 1_000_000), "ambiguous", "1380 is -8 %: too close to rule out");
  const blank = player("Gearleer", kaisinel);
  profileOf(blank, 1500);
  assert.equal(await processMatch(blank, gearAnswer("Gearleer", { charAaaa1: 1520, charBbbb2: null }), 1_000_000), "ambiguous");
  const none = player("Gearohne", kaisinel);
  assert.equal(await processMatch(none, gearAnswer("Gearohne", { charAaaa1: 1520, charBbbb2: 2300 }), 1_000_000), "ambiguous");
  const fromNc = player("Gearnc", kaisinel);
  profileOf(fromNc, 1500, "nc");
  assert.equal(await processMatch(fromNc, gearAnswer("Gearnc", { charAaaa1: 1520, charBbbb2: 2300 }), 1_000_000), "ambiguous", "NC's own number proves nothing about NC's characters");
});
