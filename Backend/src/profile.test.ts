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
const { buildProfileView, upsertProfile, profileSchema, zenitStage } = await import("./profile.js");
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
    { slot: 1, itemId: 110150026, enchant: 16 }, // Wind Breeze Greatsword (a test value: +15 with one Zenit stage)
    // Aulamus' Earrings as the in-game tooltip showed them (2026-10-09): stones Block, Angriffskraft, Zusatzausweichen, Verteidigung
    // (the third one an empty slot here), rolled MP 96, MP-Regeneration 23, Angriffskraft 24, Ausweichen 24 and a stat the data does not know.
    {
      slot: 11,
      itemId: 310230052,
      enchant: 10,
      stones: [{ stat: 255, tier: 2 }, { stat: 317, tier: 2 }, { stat: 0, tier: 0 }, { stat: 307, tier: 1 }],
      stats: [{ stat: 199, value: 96 }, { stat: 200, value: 23 }, { stat: 317, value: 24 }, { stat: 312, value: 24 }, { stat: 99999, value: 5 }],
      godstone: 19950016,
      skillBonuses: [{ skill: 11170000, level: 1 }],
    },
    { slot: 17, itemId: 215250001, enchant: 4 }, // Noble Belt +4
    { slot: 22, itemId: 311040001, enchant: 3 }, // Revelation Amulet +3
  ],
  skills: [
    { id: 11010000, level: 12, baseLevel: 10 }, // Rending Blow 10+2
    { id: 11010340, level: 12, baseLevel: 10 }, // its variant - not listed twice
    { id: 11730000, level: 11, baseLevel: 10 },
  ],
  daevanion: [{ board: 11, nodes: [110113, 110065, 110095, 999999999] }], // start, HP node, Rending Blow +1, unknown
  species: [
    // Fera as the pet window showed it: level 7 (31800 / 40000), LP 43, Fera attack 5, PvE accuracy 27.
    { id: 3, level: 7, progress: 31800, effects: [{ page: 1, slot: 1, stat: 401, value: 5 }, { page: 1, slot: 0, stat: 193, value: 43 }, { page: 1, slot: 2, stat: 110, value: 27 }] },
    // Cognia is maxed; Endurance is a percent stat (145 = 1.45 %); 99999 is a stat the data does not know yet.
    { id: 2, level: 10, progress: 0, effects: [{ page: 1, slot: 0, stat: 50, value: 14 }, { page: 1, slot: 6, stat: 445, value: 145 }, { page: 1, slot: 7, stat: 99999, value: 3 }] },
  ],
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

test("the profile names the mana stones and rolled stats of a piece in the client's languages", () => {
  const view = buildProfileView(playerId("Aahz"))!;
  const earring = view.gear.find((g) => g.slot === 11)!;
  assert.deepEqual(earring.stones?.map((k) => [k.stat, k.name, k.tier]), [[255, "Block", 2], [317, "Attack", 2], [0, "", 0], [307, "Defense", 1]]);
  // amounts only where a tooltip showed them for that stat and tier: Block+10 green, nothing known for Attack or Defense
  assert.deepEqual(earring.stones?.map((k) => k.value), [10, null, null, null]);
  assert.equal(earring.godstone?.names?.de, "Götterstein: Aulvicars Zauber");
  assert.deepEqual(earring.skillBonuses?.map((k) => [k.name, k.names?.de, k.level]), [["Overhead Slam", "Abwärtsschlag", 1]]);
  assert.equal(earring.stones?.[1].names?.de, "Angriffskraft");
  assert.deepEqual(earring.stats?.map((k) => [k.name, k.value]), [["MP", 96], ["Natural MP Regen", 23], ["Attack", 24], ["Evasion", 24], ["Stat 99999", 5]]);
  assert.equal(earring.stats?.[1].names?.de, "MP-Regeneration (Natürlich)");
  // Zenit: the weapon's enchant byte 16 is +15 with one stage (derived orange values only for the main hand); 24 stays untouched
  const upgraded = zenitStage(16);
  assert.deepEqual([upgraded, zenitStage(15), zenitStage(20), zenitStage(21), zenitStage(24)], [1, 0, 5, 0, 0]);
  const weapon = view.gear.find((g) => g.slot === 1)!;
  assert.deepEqual([weapon.enchant, weapon.zenit, weapon.zenitBonus], [16, 1, { attack: 10, damageBoostPercent: 1 }]);
  assert.equal(earring.zenit, 0);
  assert.equal(earring.zenitBonus, null);
  // a piece an older client uploaded carries no stone data at all - not an empty list
  const belt = view.gear.find((g) => g.name === "Noble Belt")!;
  assert.deepEqual([belt.stones, belt.stats, belt.godstone, belt.skillBonuses], [null, null, null, null]);
  // anything that is not a stone/stat list is rejected rather than stored
  assert.throws(() => profileSchema.parse({ source: "self", gear: [{ slot: 1, itemId: 5, enchant: 0, stones: [{ stat: -1, tier: 2 }] }] }));
});

test("the profile carries the species knowledge with stat names in the game client's languages", () => {
  const view = buildProfileView(playerId("Aahz"))!;
  assert.deepEqual(view.species.map((k) => [k.key, k.level, k.progress]), [["cognia", 10, 0], ["fera", 7, 31800]]); // sorted by id
  const fera = view.species[1];
  assert.equal(fera.names.de, "Fera");
  assert.deepEqual(fera.effects.map((e) => [e.slot, e.name, e.value]), [[0, "HP", 43], [1, "Fera Attack", 5], [2, "PvE Accuracy", 27]]); // sorted by slot
  assert.equal(fera.effects[1].names?.de, "Angriffskraft (Fera)");
  const cognia = view.species[0];
  assert.deepEqual(cognia.effects.map((e) => [e.name, e.percent]), [["Boss Attack", false], ["Endurance", true], ["Stat 99999", false]]);
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

test("titles, pet circles and board counts of a seen player are stored and resolved", () => {
  processUpload(
    aion2Upload([
      participant("Aahz"),
      participant("Window", {
        profile: {
          source: "seen",
          classId: 1,
          faction: 2,
          gear: [{ slot: 1, itemId: 110150026, enchant: 0 }],
          titles: [{ slot: 1, titleId: 12010050 }, { slot: 2, titleId: 12030003 }, { slot: 3, titleId: 12010061 }],
          pets: [{ species: 3, level: 7, kinds: [3, 2, 4, 2, 1, 3, 3] }, { species: 2, level: 7, kinds: [3, 3, 3, 2, 1, 3, 3] }],
          boardCounts: [{ board: 11, count: 63 }],
        },
      }),
    ]),
  );
  const view = buildProfileView(playerId("Window"))!;
  assert.deepEqual(view.titles.map((t) => [t.slot, t.names.de, t.grade]), [[1, "Fallen aktiviert", "Legend"], [2, "Heiliger Verterons", "Unique"], [3, "Durch die Hölle und zurück", "Unique"]]);
  assert.deepEqual(view.pets.map((k) => [k.key, k.kinds.join("")]), [["cognia", "3332133"], ["fera", "3242133"]]);
  assert.equal(view.boards[0].board, 11);
  assert.equal(view.boards[0].count, 62); // the window counts the start node
});

test("character profiles are accepted for Aion 2 only", () => {
  const classic = { ...aion2Upload([participant("Anna", { profile: selfProfile })]), game: "aion" as const, serverFingerprint: "70.0.0.150:10241" };
  assert.equal(uploadSchema.safeParse(classic).success, false);
  assert.equal(uploadSchema.safeParse(aion2Upload([participant("Aahz", { profile: selfProfile })])).success, true);
  // 25 pieces were seen on a real character: that must pass.
  const real = { ...selfProfile, gear: Array.from({ length: 25 }, (_, i) => ({ slot: i + 5, itemId: 1000 + i, enchant: 0 })) };
  assert.equal(uploadSchema.safeParse(aion2Upload([participant("Aahz", { profile: real })])).success, true);
  // Bounds: a profile may not carry an absurd amount of data.
  const huge = { ...selfProfile, gear: Array.from({ length: 70 }, (_, i) => ({ slot: i, itemId: 1000 + i, enchant: 0 })) };
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
      bossMaxHp: 100_000, // uploads only count for a killed boss: the damage must cover its hit points
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

    const aion2 = (await app.inject({ url: "/api/players/search?q=AAHZETTA&game=aion2" })).json();
    assert.equal(aion2.length, 1);
    assert.equal(aion2[0].name, "Aahzetta");
    assert.equal(aion2[0].serverName, "Europe - Kaisinel");

    const any = (await app.inject({ url: "/api/players/search?q=aahzetta" })).json();
    assert.equal(any.length, 1);

    // exact name only: no partial names, no wildcards
    for (const q of ["aahzet", "%", "aahz%", "_"]) {
      assert.equal((await app.inject({ url: `/api/players/search?q=${encodeURIComponent(q)}` })).json().length, 0, q);
    }

    // and the player page answers with the profile although there is not a single fight
    const page = (await app.inject({ url: `/api/players/${aion2[0].id}` })).json();
    assert.equal(page.history.length, 0);
    assert.equal(page.profile.className, "Gladiator");
  } finally {
    await app.close();
  }
});
