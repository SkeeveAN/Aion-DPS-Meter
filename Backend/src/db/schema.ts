import {
  sqliteTable,
  integer,
  text,
  real,
  uniqueIndex,
  index,
} from "drizzle-orm/sqlite-core";
import { sql } from "drizzle-orm";

// Defined here rather than imported from constants.ts: drizzle-kit loads this file through its
// own bundler, which cannot resolve project-local ".js" imports. constants.ts re-exports these.
// "aion" is the classic client (4.x private servers, Chat.log-based meter); "aion2" the UE5 client
// with entirely separate content, classes and capture path.
export const GAMES = ["aion", "aion2"] as const;
export const INSTANCE_CATEGORIES = ["expedition", "transcendence", "sanctuary", "hideout", "stronghold", "awakening", "nightmare", "ascension", "worldboss"] as const;

// Where a content row's facts come from: "curated" = entered by hand from real uploads/client
// strings (the historical default), "derived" = generated from a third-party dataset by
// scripts/derive-aion2-content.ts and not yet confirmed against a real client.
const CONTENT_SOURCES = ["curated", "derived"] as const;

// A curated, human-facing reference list of real Aion servers (official and private, researched
// against actual server sites/rankings) - deliberately separate from `servers` below. That table
// is auto-populated from a technical fingerprint the moment an upload arrives; this one exists so
// the CLIENT can offer "which server is this character on" as a picker (name + patch version)
// when a character is registered, before that character has ever uploaded anything at all. Seeded
// by migration, not user-editable from the client.
export const serverCatalog = sqliteTable("server_catalog", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  name: text("name").notNull().unique(),
  // Free text on purpose ("4.6", "4.6.2", "7.7", "1.2-2.5") - private servers don't share one
  // numbering scheme, so forcing this into a structured major/minor pair would misrepresent some
  // of them.
  version: text("version").notNull(),
  kind: text("kind", { enum: ["official", "private"] }).notNull(),
  // A server runs exactly one game; everything hanging off it (servers, players, encounters)
  // inherits the game through this row rather than repeating the column (see constants.ts GAMES).
  game: text("game", { enum: GAMES }).notNull().default("aion"),
  // URL segment ("origin-aion"); filled by the slug backfill (db/backfill.ts), never typed by hand.
  slug: text("slug"),
  // Per the user: a handful of researched entries turned out not worth offering (unclear
  // reliability) - marked inactive rather than deleted, same "never just discard a row" rule as
  // e.g. the unassigned-instance bucket. GET /api/server-catalog filters these out; the row stays
  // in the table as a record of what was researched and rejected, not silently gone.
  active: integer("active", { mode: "boolean" }).notNull().default(true),
  // Aion 2 is organised region -> server ("Europe" -> "Siel"), and the same server name exists in
  // several regions, so each server is its own row named "<Region> - <Server>" and carries its
  // region here. Null for classic Aion, which has no such level.
  region: text("region"),
  // Each Aion 2 server belongs to one faction ("Elyos" | "Asmodian"); the two halves of a pair
  // (Siel <-> Israphel) are separate rows. Null where the game has no faction-bound servers.
  faction: text("faction"),
});

// Per-private-server identity. Gear/rate standards differ completely between servers (per the
// user: EuroAion is nowhere near this server's gear level), so any table with real run data --
// players, encounters -- must be scoped to one of these and never merged or leaderboarded across
// rows with a different serverId. The bosses/encounters roster (which NPC is which boss, its
// aliases, loot rules) stays unscoped - the fight itself is the same regardless of which server
// runs it - but which INSTANCES a given server even offers is NOT the same everywhere (see
// serverCatalogInstances below): per the user, Origin Aion and EuroAion (both 4.6) share one list,
// while Aion Riftshade (4.8) has a wider one, and a level-65 server has no reason to show a
// level-40-ish instance from a much earlier patch either.
// The client's own bin64\config.ini [ServerAddr] BIND_ADDR:BIND_PORT (see the client's
// Server/ServerIdentity.cs) turned out NOT to be unique per private-server operator despite
// ServerIdentity's own docstring claiming so - a real incident had Aion Riftshade and Origin Aion
// resolve to the exact same "70.0.0.150:10241" (both reachable through the same gateway), which
// under the old `fingerprint.unique()` made the second server's every upload silently reuse -
// and relabel - the first server's own row (see matching/merge.ts's own remarks on upsertServer).
// fingerprint alone is therefore no longer unique; (fingerprint, displayName) together are - two
// operators sharing a fingerprint still get their own row as long as their displayName differs,
// which is exactly the signal a real upload always carries (see uploadSchema.ts's serverName).
export const servers = sqliteTable(
  "servers",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    fingerprint: text("fingerprint").notNull(),
    // Cosmetic label ("Origin Aion", "EuroAion"), latest upload wins - same update-in-place pattern
    // as players.name below. Null until some client sends one.
    displayName: text("display_name"),
    firstSeenAt: text("first_seen_at")
      .notNull()
      .default(sql`(current_timestamp)`),
  },
  (table) => ({
    fingerprintDisplayNameIdx: uniqueIndex("servers_fingerprint_display_name_idx").on(
      table.fingerprint,
      table.displayName,
    ),
  }),
);

// Instance names are only unique per game: "Fire Temple" exists in both Aion and Aion 2 as two
// unrelated dungeons, and the unassigned bucket (see constants.ts) exists once per game too.
export const instances = sqliteTable(
  "instances",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    // The literal name uploads carry (German for the historical Aion rows - whatever language the
    // first uploader's client ran in; English for Aion 2 rows derived from client data).
    name: text("name").notNull(),
    game: text("game", { enum: GAMES }).notNull().default("aion"),
    slug: text("slug"),
    // English display name for titles/URLs when `name` isn't English (see Web-Frontend/game-data.js).
    nameEn: text("name_en"),
    // Aion 2 groups its dungeons by kind (expedition/transcendence/…); null for Aion rows and for
    // any Aion 2 dungeon whose kind the source data didn't state - never guessed.
    category: text("category", { enum: INSTANCE_CATEGORIES }),
    source: text("source", { enum: CONTENT_SOURCES }).notNull().default("curated"),
    sortOrder: integer("sort_order").notNull().default(0),
    // Content the EU client does not offer (yet): kept, never deleted, but not listed.
    hidden: integer("hidden", { mode: "boolean" }).notNull().default(false),
    // Expedition difficulty: "explore" (normal) or "conquest" (hard) - two separate instances with
    // their own bosses and rankings. Null for everything without such a split.
    variant: text("variant", { enum: ["explore", "conquest"] }),
    createdAt: text("created_at")
      .notNull()
      .default(sql`(current_timestamp)`),
  },
  (table) => ({
    gameNameIdx: uniqueIndex("instances_game_name_idx").on(table.game, table.name),
    gameSlugIdx: uniqueIndex("instances_game_slug_idx").on(table.game, table.slug),
  }),
);

// Which instances a given catalog server actually offers - per the user, this genuinely differs
// (Origin/EuroAion share one list, Riftshade's 4.8 list is wider, the rest are unknown so far and
// deliberately left unmapped rather than guessed - same "empty beats wrong" rule as everywhere else
// in this schema). A join table rather than a column on either side: an instance can belong to
// several servers' lists (Origin and EuroAion both list Sauro) and this needs to grow server-by-
// server as the user confirms more, without ever touching the shared instances/bosses rows
// themselves. GET /api/instances filters by this when a serverCatalogId is given (see
// instances.ts) - a server with no rows here yet simply shows no instances, not every instance
// ever seen, until someone curates it (see Backend/README.md).
export const serverCatalogInstances = sqliteTable(
  "server_catalog_instances",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    serverCatalogId: integer("server_catalog_id")
      .notNull()
      .references(() => serverCatalog.id),
    instanceId: integer("instance_id")
      .notNull()
      .references(() => instances.id),
  },
  (table) => ({
    serverCatalogIdInstanceIdIdx: uniqueIndex("server_catalog_instances_pair_idx").on(
      table.serverCatalogId,
      table.instanceId,
    ),
  }),
);

export const bosses = sqliteTable(
  "bosses",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    instanceId: integer("instance_id")
      .notNull()
      .references(() => instances.id),
    name: text("name").notNull(),
    // Unique within a game (enforced by db/backfill.ts and merge.ts, not by index - the game lives
    // on the instance row): Aion 2 reuses boss names across dungeons (36 cases in the source data),
    // so two same-named bosses get instance-qualified slugs rather than colliding.
    slug: text("slug"),
    nameEn: text("name_en"),
    source: text("source", { enum: CONTENT_SOURCES }).notNull().default("curated"),
    // Alternate NPC names the same boss is known under (rank/phase variants,
    // localized client strings) - matched against an upload's bossNpcName.
    npcNameAliases: text("npc_name_aliases", { mode: "json" })
      .$type<string[]>()
      .notNull()
      .default(sql`'[]'`),
    // A boss name auto-lands here on first upload with no way to tell "real boss" from "random
    // trash mob the group happened to fight" (see merge.ts) - per the user, a regular mob (e.g.
    // "Zauberer der Stahlrose" in Steel Rose Cargo) isn't leaderboard-worthy the way the
    // instance's actual boss is. Same "mark, never delete" pattern as serverCatalog.active: the
    // row and its encounters stay intact (still reachable by direct /api/bosses/:id/leaderboard
    // link), just hidden from GET /api/instances/:id/bosses. Manually curated, same as the
    // instance mapping itself (see Backend/README.md) - never inferred automatically.
    isTrashMob: integer("is_trash_mob", { mode: "boolean" }).notNull().default(false),
    // Per the user: a solo practice/check target (nothing tied to a real group fight, e.g. a
    // Training Dummy) ranks meaningfully by INDIVIDUAL best iDPS per class - the "top 10 groups"
    // leaderboard that makes sense for a real boss would just show one-person "groups" there,
    // which is the same ranking as topByClass but presented as if it needed a roster. Manually
    // curated, same pattern as isTrashMob above (see README).
    isSolo: integer("is_solo", { mode: "boolean" }).notNull().default(false),
    // Curated, static drop-rule reference text ("bekannte Regeln") - never inferred from uploads,
    // since loot is deliberately never part of an upload payload at all (see
    // encounterParticipants' own remarks: only combat performance is uploaded). A JSON array of
    // {item, rule} rather than a separate table: this is simple, rarely-edited reference data, the
    // same reasoning npcNameAliases below already used for its own array column.
    lootRules: text("loot_rules", { mode: "json" })
      .$type<{ item: string; rule: string }[]>()
      .notNull()
      .default(sql`'[]'`),
    createdAt: text("created_at")
      .notNull()
      .default(sql`(current_timestamp)`),
  },
  (table) => ({
    instanceIdIdx: index("bosses_instance_id_idx").on(table.instanceId),
    slugIdx: index("bosses_slug_idx").on(table.slug),
  }),
);

// Aion 2 identifies NPCs by numeric id in its network traffic, and one boss has several ids (one
// per difficulty variant, e.g. 2300171/2310171/2320171). An upload carrying bossNpcId resolves
// through this table first - by-name matching is a fallback there, since names repeat across
// dungeons. difficulty is only set when the source data states it; null means unknown.
export const bossNpcIds = sqliteTable(
  "boss_npc_ids",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    bossId: integer("boss_id")
      .notNull()
      .references(() => bosses.id),
    npcId: integer("npc_id").notNull().unique(),
    difficulty: text("difficulty"),
  },
  (table) => ({ bossIdIdx: index("boss_npc_ids_boss_id_idx").on(table.bossId) }),
);

// Wipe-mechanics reference per boss (or per instance for dungeon-wide rules, then bossId is null
// and instanceId set - exactly one of the two). Trigger/severity/ordering are derived facts;
// action/detail are OUR OWN prose - the derivation script leaves them empty and a human fills them
// in, so a row with an empty action renders as "in progress", never as copied third-party text.
export const bossMechanics = sqliteTable(
  "boss_mechanics",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    bossId: integer("boss_id").references(() => bosses.id),
    instanceId: integer("instance_id").references(() => instances.id),
    sortOrder: integer("sort_order").notNull().default(0),
    // Stable key from the derivation script ("cleave", "p2-bombs") so re-runs update in place.
    key: text("key").notNull(),
    triggerType: text("trigger_type", { enum: ["phase", "hp"] }).notNull(),
    triggerPct: integer("trigger_pct"),
    triggerLabel: text("trigger_label").notNull().default(""),
    severity: text("severity", { enum: ["wipe", "wipe_avoidable", "mechanic"] }).notNull(),
    action: text("action").notNull().default(""),
    detail: text("detail").notNull().default(""),
    positionSheet: text("position_sheet", { mode: "json" }).$type<unknown>(),
    source: text("source", { enum: CONTENT_SOURCES }).notNull().default("derived"),
  },
  (table) => ({
    bossKeyIdx: uniqueIndex("boss_mechanics_boss_key_idx").on(table.bossId, table.key),
    instanceKeyIdx: uniqueIndex("boss_mechanics_instance_key_idx").on(table.instanceId, table.key),
  }),
);

// A legion: a name on one server (the game's legions belong to a server). players.guild keeps the name a player was seen with,
// players.guild_id the legion row it belongs to; the slug is assigned once and then kept, so a link never rots when members move.
export const guilds = sqliteTable(
  "guilds",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    name: text("name").notNull(),
    nameNormalized: text("name_normalized").notNull(),
    serverId: integer("server_id").notNull().references(() => servers.id),
    slug: text("slug"),
    createdAt: text("created_at").notNull().default(sql`(current_timestamp)`),
  },
  (table) => ({
    serverNameIdx: uniqueIndex("guilds_server_id_name_normalized_idx").on(table.serverId, table.nameNormalized),
    slugIdx: uniqueIndex("guilds_slug_idx").on(table.slug),
  }),
);

export const players = sqliteTable(
  "players",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    // Nullable only at the SQL level, so the add-servers migration can add this column to an
    // existing table without a full rebuild (SQLite can't ADD COLUMN ... NOT NULL with a FK
    // reference in one step); the migration backfills every existing row immediately after adding
    // it, and every code path from here on always supplies one - see matching/merge.ts upsertPlayer.
    serverId: integer("server_id").references(() => servers.id),
    // "Elyos" | "Asmodian" | "": the faction a fight or a profile upload reported for him (the latest non-empty one).
    faction: text("faction").notNull().default(""),
    guildId: integer("guild_id").references(() => guilds.id),
    // Display name, latest-seen casing. Uniqueness/lookup goes through
    // nameNormalized below since SQLite text columns compare case-sensitively
    // by default and Aion names are otherwise unique per side.
    name: text("name").notNull(),
    nameNormalized: text("name_normalized").notNull(),
    // URL segment ("aahz-kaisinel"): name plus server, since a name is only unique per server.
    // Assigned once and then stable, even if the character is renamed (links must not rot).
    slug: text("slug"),
    // The guild the player was last seen in, as the uploader's client reported it (Aion 2's
    // frames name a player's guild next to their name). Latest-seen wins, like `name`; null until
    // an upload carries one, and an upload WITHOUT one never clears it.
    guild: text("guild"),
    // Old names this same real character used to go by, normalized the same way nameNormalized
    // is - manually curated (never inferred: Aion's Chat.log never announces a rename, so there is
    // no automatic signal to detect one from). Same "mark, never guess" pattern as bosses'
    // npc_name_aliases. Exists because a rename otherwise splits one real person across two
    // `players` rows forever - found from a real report: "Alhamdulilah" and "Hidan" are the same
    // character, and a single upload for one old fight reported both as separate participants (the
    // rename happened outside that instance, cause otherwise unconfirmed - see
    // matching/merge.ts's own remarks on how this list is consulted).
    aliasNamesNormalized: text("alias_names_normalized", { mode: "json" })
      .$type<string[]>()
      .notNull()
      .default(sql`'[]'`),
    firstSeenAt: text("first_seen_at")
      .notNull()
      .default(sql`(current_timestamp)`),
    lastSeenAt: text("last_seen_at")
      .notNull()
      .default(sql`(current_timestamp)`),
  },
  (table) => ({
    // A name is only unique WITHIN one server - two independent servers can each have their own
    // "Anna", and treating them as the same player would blend two unrelated people's history.
    serverIdNameNormalizedIdx: uniqueIndex("players_server_id_name_normalized_idx").on(
      table.serverId,
      table.nameNormalized,
    ),
    slugIdx: uniqueIndex("players_slug_idx").on(table.slug),
    guildIdIdx: index("players_guild_id_idx").on(table.guildId),
  }),
);

// What an Aion 2 client could read about a character from the game's traffic: level, class and
// faction, the equipped items (with enchant level), and - only for the uploader's own character -
// the full skill list and the Daevanion boards. One row per player, replaced by newer data; the
// uploader's own record ("self") is never overwritten by something merely "seen" on another
// player (see profile.ts). The JSON columns hold ids only; names and values are resolved from the
// game data at read time, so a data fix never needs a migration.
export const playerProfiles = sqliteTable("player_profiles", {
  playerId: integer("player_id")
    .primaryKey()
    .references(() => players.id),
  source: text("source", { enum: ["self", "seen"] }).notNull(),
  updatedAt: text("updated_at")
    .notNull()
    .default(sql`(current_timestamp)`),
  level: integer("level"),
  classId: integer("class_id"),
  // The low two bits of the class code the client read (4 * class id + bits). NOT a faction: Elyos characters carry 1 and 2.
  classBits: integer("class_bits"),
  // Gear score ("Ausrüstungswert") as the game's character window shows it; known only for players whose window an uploader opened.
  gearScore: integer("gear_score"),
  gearJson: text("gear_json").notNull().default("[]"),
  skillsJson: text("skills_json").notNull().default("[]"),
  daevanionJson: text("daevanion_json").notNull().default("[]"),
  // Species knowledge (Cognia, Fera, Natura, Varia, Specia): level, progress and analysed effects; own character only.
  speciesJson: text("species_json").notNull().default("[]"),
  // Worn titles ([{slot, titleId}]), pet circles ([{species, level, kinds}]) and the activated node count per Daevanion
  // board of players whose window an uploader opened ([{board, count}]).
  titlesJson: text("titles_json").notNull().default("[]"),
  petsJson: text("pets_json").notNull().default("[]"),
  boardCountsJson: text("board_counts_json").notNull().default("[]"),
  // Character window of the uploader's own character: attribute id -> value (main attributes 1..6, lords 7..17), the worn
  // wing and wing skin (item ids) and the active pet (species id). Null/empty when the client did not send them.
  attributesJson: text("attributes_json").notNull().default("{}"),
  wingId: integer("wing_id"),
  wingSkinId: integer("wing_skin_id"),
  activePet: integer("active_pet"),
  activePetLevel: integer("active_pet_level"),
});

export const encounters = sqliteTable(
  "encounters",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    // See players.serverId above for why this is nullable at the SQL level despite every row from
    // here on always having one - matching/merge.ts never matches or creates across two different
    // serverId values, which is the whole point (see servers table's own remarks).
    serverId: integer("server_id").references(() => servers.id),
    bossId: integer("boss_id")
      .notNull()
      .references(() => bosses.id),
    startedAt: text("started_at").notNull(),
    endedAt: text("ended_at").notNull(),
    durationSeconds: real("duration_seconds").notNull(),
    // Sum(group damage) / engagement window - same definition as the client's
    // DpsCalculator.TargetIDps, recomputed on every merge.
    groupIDps: real("group_idps").notNull(),
    // Sorted, comma-separated participant names as of the last merge - a cheap
    // pre-filter before the real roster-similarity check on a new upload.
    rosterFingerprint: text("roster_fingerprint").notNull(),
    mergedUploadCount: integer("merged_upload_count").notNull().default(1),
    // Difficulty step inside one boss: Nightmare stage "1".."10", Ascension "easy".."extreme",
    // Transcendence stage "1".."4". Empty for bosses without such steps. Each step is ranked on its own.
    mode: text("mode").notNull().default(""),
    // The boss's highest hit-point reading as a client saw it (the game's own HP frame). Explore and
    // conquest differ 2-3x in it, so it tells them apart where the NPC id cannot. Null = not reported.
    bossMaxHp: integer("boss_max_hp"),
    createdAt: text("created_at")
      .notNull()
      .default(sql`(current_timestamp)`),
  },
  (table) => ({
    bossIdIdx: index("encounters_boss_id_idx").on(table.bossId),
    startedAtIdx: index("encounters_started_at_idx").on(table.startedAt),
    // The leaderboard query and the matching-candidate lookup both filter by exactly this pair.
    serverIdBossIdIdx: index("encounters_server_id_boss_id_idx").on(table.serverId, table.bossId),
  }),
);

export const encounterParticipants = sqliteTable(
  "encounter_participants",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    encounterId: integer("encounter_id")
      .notNull()
      .references(() => encounters.id),
    playerId: integer("player_id")
      .notNull()
      .references(() => players.id),
    className: text("class_name").notNull(),
    faction: text("faction").notNull().default(""),
    totalDamage: integer("total_damage").notNull(),
    dps: real("dps").notNull(),
    idps: real("idps").notNull(),
    // Per the user: AP/Kinah/EXP/loot are never uploaded, only combat performance - the healing
    // half of that (damage is totalDamage/dps/idps above). No isHeal-scoped crit rate alongside
    // critRatePercent below: that one column has only ever meant the damage crit rate, and heals
    // can crit too, so overloading it would silently conflate two different rates.
    totalHealing: integer("total_healing").notNull().default(0),
    hps: real("hps").notNull().default(0),
    // Opposite direction from totalDamage (dealt TO the boss) - how much of the boss's own damage
    // this participant absorbed, over the same window. Powers the frontend's damage-distribution
    // chart (who ate the boss's hits, not who hit the boss - an aggro/tank question totalDamage
    // cannot answer). Defaults to 0 for rows from a client older than this column.
    damageTaken: integer("damage_taken").notNull().default(0),
    // Group shields (Chanter, Templar): damage that shields placed on this participant soaked up
    // (on top of damageTaken, which is what got through), and for the caster who got how much.
    // Empty until a client reports them.
    damageAbsorbed: integer("damage_absorbed").notNull().default(0),
    shieldsGiven: text("shields_given", { mode: "json" }).$type<{ playerName: string; amount: number }[]>().notNull().default(sql`'[]'`),
    critRatePercent: real("crit_rate_percent").notNull(),
    // True once this participant's own upload (isSelf) supplied the crit rate -
    // Aion only flags crits reliably in the scorer's own log (see the client's
    // Combat/CritEstimator.cs), so a self-reported value always wins over an
    // estimate from someone else's log and is never downgraded again.
    isCritRateAuthoritative: integer("is_crit_rate_authoritative", { mode: "boolean" })
      .notNull()
      .default(false),
  },
  (table) => ({
    encounterIdIdx: index("encounter_participants_encounter_id_idx").on(table.encounterId),
    playerIdIdx: index("encounter_participants_player_id_idx").on(table.playerId),
  }),
);

export const encounterSkillUsage = sqliteTable(
  "encounter_skill_usage",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    participantId: integer("participant_id")
      .notNull()
      .references(() => encounterParticipants.id),
    skillName: text("skill_name").notNull(),
    hits: integer("hits").notNull(),
    critHits: integer("crit_hits").notNull(),
    // Column names kept as damage-flavored (totalDamage/minHit/maxHit) rather than renamed to
    // something neutral - renaming an existing column forces a full SQLite table rebuild for a
    // purely cosmetic gain, whereas isHeal below is the actual, load-bearing distinction: it's what
    // separates a participant's damage skill breakdown from their heal skill breakdown, both stored
    // in this one table rather than a duplicated parallel one.
    totalDamage: integer("total_damage").notNull(),
    minHit: integer("min_hit").notNull(),
    maxHit: integer("max_hit").notNull(),
    isHeal: integer("is_heal", { mode: "boolean" }).notNull().default(false),
  },
  (table) => ({
    participantIdIdx: index("encounter_skill_usage_participant_id_idx").on(table.participantId),
  }),
);

// Real reinforcements (buffs) a participant RECEIVED - see the client's ChatLog/BuffCastEvent for
// how these are decoded ("X is in the boost ... state because Y used Z", distinguished from a
// debuff purely by that one word in Aion's own narration) and keyed by recipient rather than
// caster, so a Cleric/Chanter's group-wide buff is attributed to every party member it actually
// landed on instead of only whoever cast it. Deliberately a separate table from
// encounterSkillUsage above rather than another isHeal-style flag on it: a buff cast has no
// damage/crit/min/max to report, and those columns being NOT NULL there would force meaningless
// zeros rather than leaving them out entirely.
export const encounterBuffUsage = sqliteTable(
  "encounter_buff_usage",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    participantId: integer("participant_id")
      .notNull()
      .references(() => encounterParticipants.id),
    skillName: text("skill_name").notNull(),
    casts: integer("casts").notNull(),
  },
  (table) => ({
    participantIdIdx: index("encounter_buff_usage_participant_id_idx").on(table.participantId),
  }),
);

export const uploads = sqliteTable(
  "uploads",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    receivedAt: text("received_at")
      .notNull()
      .default(sql`(current_timestamp)`),
    clientVersion: text("client_version").notNull().default(""),
    // Set on the success path only (see routes/uploads.ts) - a payload that fails schema
    // validation or throws before a server row could be resolved leaves this null, which is fine:
    // it's an audit log entry, not something a leaderboard ever reads.
    serverId: integer("server_id").references(() => servers.id),
    // What the client reported about the server: the numeric id of the own character record and the game
    // server's address (IP:port). The id is reused by every region, the address tells them apart.
    clientServerId: integer("client_server_id"),
    gameServer: text("game_server"),
    uploaderReportedName: text("uploader_reported_name").notNull(),
    // Salted hash, never the raw IP - only used for rate-limit bookkeeping/abuse review.
    ipHash: text("ip_hash").notNull(),
    matchedEncounterId: integer("matched_encounter_id").references(() => encounters.id),
    status: text("status", { enum: ["pending", "merged", "rejected"] })
      .notNull()
      .default("pending"),
    // Raw payload as received, kept for audit and so the merge algorithm can be
    // improved later and re-run over history without asking clients to re-upload.
    rawPayloadJson: text("raw_payload_json").notNull(),
  },
  (table) => ({
    matchedEncounterIdIdx: index("uploads_matched_encounter_id_idx").on(table.matchedEncounterId),
  }),
);

// One row per click on the site's download button (routes/downloads.ts). Downloads the client's own
// updater makes from GitHub never pass through the site and are not counted here.
export const downloads = sqliteTable(
  "downloads",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    downloadedAt: text("downloaded_at")
      .notNull()
      .default(sql`(current_timestamp)`),
    ipHash: text("ip_hash").notNull(),
    tag: text("tag").notNull(),
    isBot: integer("is_bot", { mode: "boolean" }).notNull().default(false),
  },
  (table) => ({
    downloadedAtIdx: index("downloads_downloaded_at_idx").on(table.downloadedAt),
  }),
);

// Cut-out character portrait from NC's public profile image (see portraitService.ts): the status of the last
// attempt per player; the picture itself is the file data/portraits/<player_id>.png. A player without a row has not been tried yet.
export const playerPortraits = sqliteTable("player_portraits", {
  playerId: integer("player_id")
    .primaryKey()
    .references(() => players.id, { onDelete: "cascade" }),
  // NC's own ids of the character found (null while status is not 'ok').
  charKey: text("char_key"),
  ncServerId: integer("nc_server_id"),
  // 'ok' picture exists, 'none' no unambiguous character found at NC, 'error' network or image problem.
  status: text("status").notNull(),
  fetchedAt: integer("fetched_at").notNull(),
  nextTryAt: integer("next_try_at").notNull(),
});

// Character data from NC's official character page (see ncCharacter.ts / portraitService.ts): main attributes and lord values,
// wing, wing skin, active pet with level. One row per player that was tried. Separate from player_portraits because it
// refreshes faster (the values change quicker than the looks) and keeps the character id so a refresh needs no new search.
export const playerNcCharacter = sqliteTable("player_nc_character", {
  playerId: integer("player_id")
    .primaryKey()
    .references(() => players.id, { onDelete: "cascade" }),
  // NC's url-decoded character id (base64url) and server id of the character found.
  characterId: text("character_id"),
  ncServerId: integer("nc_server_id"),
  // Same ids as player_profiles.attributes_json ("1" STR .. "17" Space); '{}' until a fetch succeeded.
  attributesJson: text("attributes_json").notNull().default("{}"),
  wingId: integer("wing_id"),
  wingSkinId: integer("wing_skin_id"),
  activePet: integer("active_pet"),
  activePetLevel: integer("active_pet_level"),
  // 'ok' data fetched, 'none' no unambiguous character found at NC, 'error' network/answer problem. Old data stays on show on a later failure.
  status: text("status").notNull(),
  // Time of the last successful fetch (of the first attempt while there is no data yet).
  fetchedAt: integer("fetched_at").notNull(),
  nextTryAt: integer("next_try_at").notNull(),
});
