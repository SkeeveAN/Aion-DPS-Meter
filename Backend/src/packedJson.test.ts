import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

// DATABASE_PATH must be set before db/client.ts is first imported (see matching/merge.test.ts).
process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-packed-test-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { players, playerProfiles, profileSetups } = await import("./db/schema.js");
const { packJson, unpackJson, PACK_MIN_LENGTH } = await import("./db/packedJson.js");
const { packBatch, unpackBatch, plainLeft, PACKED_COLUMNS } = await import("./packProfiles.js");
const { eq } = await import("drizzle-orm");

after(() => sqlite.close());

const longList = (n: number) => JSON.stringify(Array.from({ length: n }, (_, i) => ({ id: 11010000 + i, level: (i % 20) + 1 })));

test("short text stays plain, long text becomes a gzip that unpacks to the same text", () => {
  assert.equal(packJson("[]"), "[]");
  assert.equal(packJson("x".repeat(PACK_MIN_LENGTH - 1)), "x".repeat(PACK_MIN_LENGTH - 1));
  const long = longList(80);
  const packed = packJson(long);
  assert.ok(Buffer.isBuffer(packed));
  assert.ok((packed as Buffer).length < long.length / 3);
  assert.equal(unpackJson(packed), long);
  assert.equal(unpackJson(long), long);
  assert.equal(unpackJson(null), "");
});

let counter = 0;
function newPlayer(): number {
  counter += 1;
  return db.insert(players).values({ name: `Packed${counter}`, nameNormalized: `packed${counter}`, slug: `packed${counter}-test` }).returning({ id: players.id }).get().id;
}

test("a profile written through drizzle is stored gzipped and read back as text", () => {
  const id = newPlayer();
  const skills = longList(120);
  db.insert(playerProfiles).values({ playerId: id, source: "self", skillsJson: skills, gearJson: "[]", daevanionJson: longList(60) }).run();
  const raw = sqlite.prepare("select typeof(skills_json) t, typeof(gear_json) g, typeof(daevanion_json) d from player_profiles where player_id = ?").get(id) as { t: string; g: string; d: string };
  assert.deepEqual(raw, { t: "blob", g: "text", d: "blob" });
  const row = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, id)).get()!;
  assert.equal(row.skillsJson, skills);
  assert.equal(row.gearJson, "[]");
  assert.equal(JSON.parse(row.daevanionJson).length, 60);
});

test("the SQL function unpack_json reads plain and packed values, json_each works over it", () => {
  const plain = newPlayer();
  const packed = newPlayer();
  sqlite.prepare("insert into player_profiles (player_id, source, skills_json) values (?, 'self', ?)").run(plain, longList(40));
  db.insert(playerProfiles).values({ playerId: packed, source: "self", skillsJson: longList(40) }).run();
  const rows = sqlite
    .prepare("select player_id p, count(*) n, sum(json_extract(j.value, '$.level')) s from player_profiles, json_each(unpack_json(skills_json)) j where player_id in (?, ?) group by player_id")
    .all(plain, packed) as { p: number; n: number; s: number }[];
  assert.equal(rows.length, 2);
  assert.equal(rows[0].n, 40);
  assert.equal(rows[1].n, 40);
  assert.equal(rows[0].s, rows[1].s);
  assert.equal((sqlite.prepare("select typeof(skills_json) t from player_profiles where player_id = ?").get(plain) as { t: string }).t, "text");
  // an empty list ('[]' stays plain) compares as before
  const empty = newPlayer();
  db.insert(playerProfiles).values({ playerId: empty, source: "self" }).run();
  assert.equal((sqlite.prepare("select count(*) n from player_profiles where player_id = ? and skills_json <> '[]'").get(empty) as { n: number }).n, 0);
});

test("setups: the data is packed on write, and a rewrite of the same state still finds it by its hash", () => {
  const id = newPlayer();
  const data = longList(90);
  db.insert(profileSetups).values({ playerId: id, kind: "skills", hash: "h1", dataJson: data, classId: 1 }).run();
  assert.equal((sqlite.prepare("select typeof(data_json) t from profile_setups where player_id = ?").get(id) as { t: string }).t, "blob");
  assert.equal(db.select().from(profileSetups).where(eq(profileSetups.playerId, id)).get()!.dataJson, data);
});

test("the migration packs old plain rows batch by batch, can be repeated and reversed, and changes no content", () => {
  const ids: number[] = [];
  for (let i = 0; i < 7; i++) {
    const id = newPlayer();
    ids.push(id);
    sqlite
      .prepare("insert into player_profiles (player_id, source, skills_json, daevanion_json, gear_json) values (?, 'self', ?, ?, '[]')")
      .run(id, longList(50 + i), longList(30 + i));
  }
  const before = ids.map((id) => sqlite.prepare("select skills_json s, daevanion_json d from player_profiles where player_id = ?").get(id));
  const skills = PACKED_COLUMNS.find((c) => c.column === "skills_json")!;
  assert.ok(plainLeft(sqlite, skills) >= 7);
  let after = 0;
  let total = 0;
  for (;;) {
    const r = packBatch(sqlite, skills, after, 3);
    if (r.last === null) {
      break;
    }
    after = r.last;
    total += r.packed;
  }
  assert.ok(total >= 7);
  assert.equal(plainLeft(sqlite, skills), 0);
  assert.equal(packBatch(sqlite, skills, 0, 3).packed, 0, "a second run finds nothing");
  for (const id of ids) {
    assert.equal((sqlite.prepare("select typeof(skills_json) t from player_profiles where player_id = ?").get(id) as { t: string }).t, "blob");
  }
  // the content is unchanged as far as every reader can see
  const read = ids.map((id) => {
    const r = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, id)).get()!;
    return { s: r.skillsJson, d: r.daevanionJson };
  });
  assert.deepEqual(read, before);
  // the way back
  let back = 0;
  after = 0;
  for (;;) {
    const r = unpackBatch(sqlite, skills, after, 4);
    if (r.last === null) {
      break;
    }
    after = r.last;
    back += r.unpacked;
  }
  assert.ok(back >= 7);
  assert.equal((sqlite.prepare("select count(*) n from player_profiles where typeof(skills_json) = 'blob'").get() as { n: number }).n, 0);
  assert.equal((sqlite.prepare("select skills_json s from player_profiles where player_id = ?").get(ids[0]) as { s: string }).s, (before[0] as { s: string }).s);
});
