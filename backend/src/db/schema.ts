import {
  sqliteTable,
  integer,
  text,
  real,
  uniqueIndex,
  index,
} from "drizzle-orm/sqlite-core";
import { sql } from "drizzle-orm";

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
    // Display name, latest-seen casing. Uniqueness/lookup goes through
    // nameNormalized below since SQLite text columns compare case-sensitively
    // by default and Aion names are otherwise unique per side.
    name: text("name").notNull(),
    nameNormalized: text("name_normalized").notNull(),
    firstSeenAt: text("first_seen_at")
      .notNull()
      .default(sql`(current_timestamp)`),
    lastSeenAt: text("last_seen_at")
      .notNull()
      .default(sql`(current_timestamp)`),
  },
  (table) => ({ nameNormalizedIdx: uniqueIndex("players_name_normalized_idx").on(table.nameNormalized) }),
);

export const encounters = sqliteTable(
  "encounters",
  {
    id: integer("id").primaryKey({ autoIncrement: true }),
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
    critRatePercent: real("crit_rate_percent").notNull(),
    // True once this participant's own upload (isSelf) supplied the crit rate -
    // Aion only flags crits reliably in the scorer's own log (see the client's
    // Combat/CritEstimator.cs), so a self-reported value always wins over an
    // estimate from someone else's log and is never downgraded again.
    isCritRateAuthoritative: integer("is_crit_rate_authoritative", { mode: "boolean" })
      .notNull()
      .default(false),
    apTotal: integer("ap_total"),
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
    totalDamage: integer("total_damage").notNull(),
    minHit: integer("min_hit").notNull(),
    maxHit: integer("max_hit").notNull(),
  },
  (table) => ({
    participantIdIdx: index("encounter_skill_usage_participant_id_idx").on(table.participantId),
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
