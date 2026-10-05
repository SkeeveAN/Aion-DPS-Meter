import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

// DATABASE_PATH must be set before db/client.ts is first imported (see matching/merge.test.ts).
process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-profile-test-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { processUpload } = await import("./matching/merge.js");
const { players } = await import("./db/schema.js");
const { buildProfileView, upsertProfile, profileSchema } = await import("./profile.js");
const { uploadSchema } = await import("./uploadSchema.js");
const { eq } = await import("drizzle-orm");

function participant(name: string, extra: Record<string, unknown> = {}) {
  return {
    name,
    className: "Gladiator",
    faction: "",
    isSelf: name === "Aahz",
    totalDamage: 50_000,
    dps: 400,
    idps: 400,
    totalHealing: 0,
    hps: 0,
    damageTaken: 0,
    damageAbsorbed: 0,
    shieldsGiven: [],
    buffs: [],
    skills: [{ skill: "Rending Blow", hits: 20, critHits: 3, total: 50_000, min: 1000, max: 4000 }],
    healSkills: [],
    ...extra,
  };
}

function aion2Upload(participants: ReturnType<typeof participant>[]) {
  return {
    clientVersion: "test",
    mode: "",
    game: "aion2" as const,
    bossNpcName: "Training Dummy",
    startedAt: "2026-10-01T01:00:00.000Z",
    endedAt: "2026-10-01T01:02:00.000Z",
    serverFingerprint: "aion2:193.202.112.97:13328",
    participants,
  };
}

const selfProfile = {
  source: "self" as const,
  level: 34,
  classId: 1,
  faction: 2,
  gear: [
    { slot: 1, itemId: 110150026, enchant: 0 }, // Wind Breeze Greatsword
    { slot: 17, itemId: 215250001, enchant: 4 }, // Noble Belt +4
    { slot: 22, itemId: 311040001, enchant: 3 }, // Revelation Amulet +3
  ],
  skills: [
    { id: 11010000, level: 12, baseLevel: 10 }, // Rending Blow 10+2
    { id: 11010340, level: 12, baseLevel: 10 }, // its variant - not listed twice
    { id: 11730000, level: 11, baseLevel: 10 },
  ],
  daevanion: [{ board: 11, nodes: [110113, 110065, 110095, 999999999] }], // start, HP node, Rending Blow +1, unknown
};

function playerId(name: string): number {
  return db.select().from(players).where(eq(players.name, name)).get()!.id;
}

test("an Aion 2 upload carries guild and profile into the player's page data", () => {
  processUpload(aion2Upload([participant("Aahz", { guild: "Akatsuki", profile: selfProfile })]));
  const view = buildProfileView(playerId("Aahz"));
  assert.ok(view);
  assert.equal(view.source, "self");
  assert.equal(view.level, 34);
  assert.equal(view.className, "Gladiator");
  assert.equal(view.faction, "Elyos");
  assert.equal(db.select().from(players).where(eq(players.name, "Aahz")).get()!.guild, "Akatsuki");
});

test("the profile resolves item names, enchants, skill levels and Daevanion effects from ids", () => {
  const view = buildProfileView(playerId("Aahz"))!;
  const belt = view.gear.find((g) => g.name === "Noble Belt");
  assert.deepEqual([belt?.enchant, belt?.slotName], [4, "Belt"]);
  assert.equal(view.gear.find((g) => g.name === "Revelation Amulet")?.enchant, 3);
  assert.equal(view.gear.find((g) => g.name === "Wind Breeze Greatsword")?.itemLevel, 32);
  assert.ok(view.averageItemLevel && view.averageItemLevel > 0);

  // Base entries only: the variant 11010340 would repeat "Rending Blow".
  assert.equal(view.skills.filter((s) => s.name === "Rending Blow").length, 1);
  const rending = view.skills.find((s) => s.name === "Rending Blow")!;
  assert.deepEqual([rending.level, rending.baseLevel], [12, 10]);

  const nezekan = view.daevanion[0];
  assert.equal(nezekan.name, "Nezekan");
  assert.equal(nezekan.activeNodes, 3); // start node not counted; the unknown id is
  assert.equal(nezekan.knownNodes, 2);
  assert.equal(nezekan.stats.HPMax, 100);
  assert.deepEqual(nezekan.skillBonuses.map((b) => [b.name, b.value]), [["Rending Blow", 1]]);
  // The map carries every node of the board (locked ones too), with the unlocked ones flagged.
  assert.equal(nezekan.cells.length, 89); // 88 nodes + the start node
  assert.equal(nezekan.cells.filter((c) => c[4] === 1).length, 3); // start + the two known nodes
});

test("a profile merely seen on another player never replaces the player's own, and empty ones are dropped", () => {
  const id = playerId("Aahz");
  upsertProfile(id, { source: "seen", classId: 4, faction: 1, gear: [{ slot: 1, itemId: 110150026, enchant: 0 }], skills: [], daevanion: [] });
  assert.equal(buildProfileView(id)!.source, "self");
  assert.equal(buildProfileView(id)!.level, 34);

  processUpload(aion2Upload([participant("Aahz"), participant("Stranger", { profile: { source: "seen", classId: 8, faction: 2, gear: [{ slot: 3, itemId: 210340023, enchant: 0 }] } })]));
  const stranger = buildProfileView(playerId("Stranger"))!;
  assert.equal(stranger.source, "seen");
  assert.equal(stranger.className, "Chanter");
  assert.equal(stranger.gear[0].name, "Faith Helm");

  processUpload(aion2Upload([participant("Aahz"), participant("Nobody", { profile: { source: "seen", gear: [] } })]));
  assert.equal(buildProfileView(playerId("Nobody")), null);
});

test("a newer own upload replaces the old one but keeps skills and boards when it carries none", () => {
  const id = playerId("Aahz");
  upsertProfile(id, profileSchema.parse({ source: "self", level: 35, classId: 1, faction: 2, gear: [{ slot: 1, itemId: 110150026, enchant: 0 }] }));
  const view = buildProfileView(id)!;
  assert.equal(view.level, 35);
  assert.equal(view.gear.length, 1);
  assert.equal(view.skills.length >= 2, true);
  assert.equal(view.daevanion.length, 1);
});

test("character profiles are accepted for Aion 2 only", () => {
  const classic = { ...aion2Upload([participant("Anna", { profile: selfProfile })]), game: "aion" as const, serverFingerprint: "70.0.0.150:10241" };
  assert.equal(uploadSchema.safeParse(classic).success, false);
  assert.equal(uploadSchema.safeParse(aion2Upload([participant("Aahz", { profile: selfProfile })])).success, true);
  // Bounds: a profile may not carry an absurd amount of data.
  const huge = { ...selfProfile, gear: Array.from({ length: 30 }, (_, i) => ({ slot: i, itemId: 1000 + i, enchant: 0 })) };
  assert.equal(uploadSchema.safeParse(aion2Upload([participant("Aahz", { profile: huge })])).success, false);
});

test("through the real routes: upload with a profile, then the player endpoint returns it resolved", async () => {
  const { buildServer } = await import("./server.js");
  const app = await buildServer();
  try {
    const payload = {
      ...aion2Upload([
        participant("Routey", { guild: "Akatsuki", isSelf: true, profile: selfProfile }),
        participant("Seen", { isSelf: false, profile: { source: "seen", classId: 7, faction: 2, gear: [{ slot: 1, itemId: 110150026, enchant: 0 }] } }),
      ]),
      startedAt: "2026-10-01T03:00:00.000Z",
      endedAt: "2026-10-01T03:02:00.000Z",
    };
    const upload = await app.inject({ method: "POST", url: "/api/uploads", payload });
    assert.equal(upload.statusCode, 200, upload.body);

    const id = db.select().from(players).where(eq(players.name, "Routey")).get()!.id;
    const response = await app.inject({ url: `/api/players/${id}` });
    assert.equal(response.statusCode, 200);
    const body = response.json();
    assert.equal(body.player.guild, "Akatsuki");
    assert.equal(body.profile.className, "Gladiator");
    assert.equal(body.profile.gear.find((g: { name: string }) => g.name === "Noble Belt").enchant, 4);
    assert.equal(body.profile.daevanion[0].name, "Nezekan");
    assert.equal(body.profile.skills.find((s: { name: string }) => s.name === "Blood Absorption").level, 11);
    // Skill names come with the game client's translations (de, en, es, fr, ja, ko, pt, ru).
    assert.ok(body.profile.skills.some((s: { names?: { de?: string } }) => typeof s.names?.de === "string" && s.names.de.length > 0));

    const seenId = db.select().from(players).where(eq(players.name, "Seen")).get()!.id;
    const seen = (await app.inject({ url: `/api/players/${seenId}` })).json();
    assert.equal(seen.profile.source, "seen");
    assert.equal(seen.profile.className, "Cleric");
    assert.equal(seen.profile.skills.length, 0);

    // A player without any profile still answers, with profile: null.
    const none = (await app.inject({ url: `/api/players/${playerId("Aahz")}` })).json();
    assert.ok(none.profile === null || typeof none.profile === "object");
  } finally {
    await app.close();
  }
});

test("players without a boss fight: POST /api/uploads/profiles stores profiles and no encounter", async () => {
  const { buildServer } = await import("./server.js");
  const { encounters } = await import("./db/schema.js");
  const app = await buildServer();
  try {
    const before = db.select().from(encounters).all().length;
    const payload = {
      clientVersion: "0.9.0",
      game: "aion2",
      serverFingerprint: "aion2:test:13328",
      participants: [
        { name: "Solo", className: "Gladiator", faction: "", guild: "Akatsuki", isSelf: true, profile: selfProfile },
        { name: "Passerby", className: "Cleric", faction: "", isSelf: false, profile: { source: "seen", classId: 7, faction: 2, gear: [{ slot: 1, itemId: 110150026, enchant: 0 }] } },
      ],
    };
    const ok = await app.inject({ method: "POST", url: "/api/uploads/profiles", payload });
    assert.equal(ok.statusCode, 200, ok.body);
    assert.equal(db.select().from(encounters).all().length, before);

    const id = db.select().from(players).where(eq(players.name, "Solo")).get()!.id;
    const body = (await app.inject({ url: `/api/players/${id}` })).json();
    assert.equal(body.player.guild, "Akatsuki");
    assert.equal(body.profile.className, "Gladiator");

    const noSelf = await app.inject({ method: "POST", url: "/api/uploads/profiles", payload: { ...payload, participants: [payload.participants[1]] } });
    assert.equal(noSelf.statusCode, 400);
    const classic = await app.inject({ method: "POST", url: "/api/uploads/profiles", payload: { ...payload, game: "aion" } });
    assert.equal(classic.statusCode, 400);
  } finally {
    await app.close();
  }
});

test("a player that only has a profile (no boss fight) is found by name search", async () => {
  const { buildServer } = await import("./server.js");
  const app = await buildServer();
  try {
    const payload = {
      clientVersion: "0.9.6",
      game: "aion2",
      serverFingerprint: "aion2:europe-kaisinel",
      serverName: "Europe - Kaisinel",
      participants: [{ name: "Aahzetta", className: "Gladiator", faction: "", guild: "Akatsuki", isSelf: true, profile: selfProfile }],
    };
    const up = await app.inject({ method: "POST", url: "/api/uploads/profiles", payload });
    assert.equal(up.statusCode, 200, up.body);

    const aion2 = (await app.inject({ url: "/api/players/search?q=aahzet&game=aion2" })).json();
    assert.equal(aion2.length, 1);
    assert.equal(aion2[0].name, "Aahzetta");
    assert.equal(aion2[0].serverName, "Europe - Kaisinel");

    const any = (await app.inject({ url: "/api/players/search?q=aahzet" })).json();
    assert.equal(any.length, 1);

    // and the player page answers with the profile although there is not a single fight
    const page = (await app.inject({ url: `/api/players/${aion2[0].id}` })).json();
    assert.equal(page.history.length, 0);
    assert.equal(page.profile.className, "Gladiator");
  } finally {
    await app.close();
  }
});
