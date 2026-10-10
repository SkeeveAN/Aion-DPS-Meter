import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-guilds-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { assignGuildSlugs, findGuild, linkPlayerGuild, listGuilds, syncGuilds } = await import("./guilds.js");
const { players, servers } = await import("./db/schema.js");
const { eq } = await import("drizzle-orm");

const server = (name: string) => Number(db.insert(servers).values({ fingerprint: `aion2:${name.toLowerCase().replace(/\W+/g, "-")}`, displayName: name }).run().lastInsertRowid);
const kaisinel = server("Europe - Kaisinel");
const beritra = server("Europe - Beritra");
let n = 0;
function player(name: string, serverId: number, guild: string | null) {
  const id = Number(db.insert(players).values({ name, nameNormalized: name.toLowerCase(), serverId, guild, slug: `${name.toLowerCase()}-${++n}` }).run().lastInsertRowid);
  if (guild) linkPlayerGuild(id);
  return id;
}
const legions = () => listGuilds();

test("a legion is a name on one server: the same name on two servers is two legions with their own links", () => {
  player("Anna", kaisinel, "Nordwind");
  player("Bernd", kaisinel, "Nordwind");
  player("Cora", beritra, "Nordwind");
  const found = legions().filter((g) => g.name === "Nordwind");
  assert.equal(found.length, 2);
  assert.deepEqual(found.map((g) => g.memberCount).sort(), [1, 2]);
  assert.notEqual(found[0].slug, found[1].slug);
  assert.ok(found.some((g) => g.slug === "nordwind-europe-kaisinel") && found.some((g) => g.slug === "nordwind-europe-beritra"));
});

test("the spelling of a legion name does not split it", () => {
  player("Dirk", kaisinel, "Eisenfaust");
  player("Edda", kaisinel, "EISENFAUST ");
  assert.equal(legions().filter((g) => g.name.toLowerCase() === "eisenfaust").length, 1);
  assert.equal(legions().find((g) => g.name.toLowerCase() === "eisenfaust")!.memberCount, 2);
});

test("a legion that only one player belongs to moves with him and keeps its link", () => {
  const id = player("Fritz", kaisinel, "Einzelgaenger");
  const before = legions().find((g) => g.name === "Einzelgaenger")!;
  db.update(players).set({ serverId: beritra }).where(eq(players.id, id)).run();
  assert.equal(syncGuilds() >= 1, true);
  const after = legions().find((g) => g.name === "Einzelgaenger")!;
  assert.equal(after.slug, before.slug, "same link");
  assert.equal(after.serverId, beritra);
});

test("a member who moves alone starts the legion on his server and leaves the others where they are", () => {
  const a = player("Gerd", kaisinel, "Sturmwache");
  player("Hanna", kaisinel, "Sturmwache");
  db.update(players).set({ serverId: beritra }).where(eq(players.id, a)).run();
  syncGuilds();
  const found = legions().filter((g) => g.name === "Sturmwache");
  assert.equal(found.length, 2);
  assert.deepEqual(found.map((g) => `${g.serverId === kaisinel ? "K" : "B"}${g.memberCount}`).sort(), ["B1", "K1"]);
});

test("the detail page lists the members of that legion on that server only", () => {
  const found = findGuild("nordwind-europe-kaisinel")!;
  assert.deepEqual(found.members.map((m) => m.name).sort(), ["Anna", "Bernd"]);
  assert.deepEqual(findGuild("nordwind-europe-beritra")!.members.map((m) => m.name), ["Cora"]);
});

test("a link is assigned once and never changes", () => {
  const before = legions().map((g) => g.slug).sort();
  assert.equal(assignGuildSlugs(), 0);
  assert.deepEqual(legions().map((g) => g.slug).sort(), before);
});

test("a player without a legion has no link to one", () => {
  const id = player("Ida", kaisinel, null);
  const row = sqlite.prepare("select guild_id g from players where id = ?").get(id) as { g: number | null };
  assert.equal(row.g, null);
});

test("a legion whose name has letters beyond ASCII keeps its row when the migration spelled it differently", () => {
  const id = Number(db.insert(players).values({ name: "Olaf", nameNormalized: "olaf", serverId: kaisinel, guild: "NØVA", slug: "olaf-x" }).run().lastInsertRowid);
  // What the migration's SQL lower() leaves: the Ø stays capital.
  const gid = Number(sqlite.prepare("insert into guilds (name, name_normalized, server_id) values ('NØVA', 'nØva', ?)").run(kaisinel).lastInsertRowid);
  sqlite.prepare("update players set guild_id = ? where id = ?").run(gid, id);
  syncGuilds();
  assignGuildSlugs();
  const rows = sqlite.prepare("select id from guilds where name = 'NØVA'").all();
  assert.equal(rows.length, 1, "no second legion row");
  assert.equal(legions().find((g) => g.name === "NØVA")!.memberCount, 1);
});
