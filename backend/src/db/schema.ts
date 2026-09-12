import {
  sqliteTable,
  integer,
  text,
  real,
  uniqueIndex,
  index,
} from "drizzle-orm/sqlite-core";
import { sql } from "drizzle-orm";

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
  // Per the user: a handful of researched entries turned out not worth offering (unclear
  // reliability) - marked inactive rather than deleted, same "never just discard a row" rule as
  // e.g. the unassigned-instance bucket. GET /api/server-catalog filters these out; the row stays
  // in the table as a record of what was researched and rejected, not silently gone.
  active: integer("active", { mode: "boolean" }).notNull().default(true),
});

// Per-private-server identity. Gear/rate standards differ completely between servers (per the
// user: EuroAion is nowhere near this server's gear level), so any table with real run data --
// players, encounters -- must be scoped to one of these and never merged or leaderboarded across
// rows with a different serverId. Instances/bosses stay UNscoped on purpose: the raid content
// itself (names, roster) is the same regardless of which server runs it.
export const servers = sqliteTable("servers", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  // The client's own bin64\config.ini [ServerAddr] BIND_ADDR:BIND_PORT (see the client's
  // Server/ServerIdentity.cs) - stable and unique per private-server operator, since Chat.log
  // itself carries no server identity at all.
  fingerprint: text("fingerprint").notNull().unique(),
  // Cosmetic label ("Origin Aion", "EuroAion"), latest upload wins - same update-in-place pattern
  // as players.name below. Null until some client sends one.
  displayName: text("display_name"),
  firstSeenAt: text("first_seen_at")
    .notNull()
    .default(sql`(current_timestamp)`),
});

export const instances = sqliteTable("instances", {
  id: integer("id").primaryKey({ autoIncrement: true }),
  name: text("name").notNull().unique(),
  sortOrder: integer("sort_order").notNull().default(0),
  createdAt: text("created_at")
    .notNull()
    .default(sql`(current_timestamp)`),
});

export const bosses = sqliteTable(
  "bosses",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
    instanceId: integer("instance_id")
      .notNull()
      .references(() => instances.id),
    name: text("name").notNull(),
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
    // instance mapping itself (see backend/README.md) - never inferred automatically.
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
  (table) => ({ instanceIdIdx: index("bosses_instance_id_idx").on(table.instanceId) }),
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
    // Display name, latest-seen casing. Uniqueness/lookup goes through
    // nameNormalized below since SQLite text columns compare case-sensitively
    // by default and Aion names are otherwise unique per side.
    name: text("name").notNull(),
    nameNormalized: text("name_normalized").notNull(),
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
  }),
);

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
