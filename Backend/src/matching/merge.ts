import { canonicalServer, nameOf } from "../content/aion2Servers.js";
import { factionOfServerName } from "../factions.js";
import { and, eq, isNull, ne, sql } from "drizzle-orm";
import { db, sqlite } from "../db/client.js";
import {
  bossNpcIds,
  bosses,
  encounterBuffUsage,
  encounterParticipants,
  encounterSkillUsage,
  encounters,
  instances,
  players,
  servers,
} from "../db/schema.js";
import { normalizeName, jaccardSimilarity, withinRelativeTolerance } from "./roster.js";
import type { ParticipantUpload, ProfilesUploadPayload, UploadPayload } from "../uploadSchema.js";
import { UNASSIGNED_INSTANCE_NAME, type Game } from "../constants.js";
import { slugify, uniqueSlug } from "../seo/slug.js";
import { upsertProfile } from "../profile.js";
import { ensurePlayerSlug } from "../seo/playerSlug.js";
import { rehomeKnownPlayers } from "./rehome.js";
import { linkPlayerGuild, reconcileGuildWithNc } from "../guilds.js";
import { askAboutRenamed, noteClassHint, checkPlayers, noteClientServer, refreshAfterUpload } from "../ncMatch.js";

/** Time-window tolerance of the END for two encounters to even be considered the same fight (the kill is one moment for everybody). */
const TIME_TOLERANCE_SECONDS = 20;
/** The START may differ more: an uploader who joined late or whose first hit came later saw the fight begin up to half a minute after the others (three runs 20-22 s apart were not merged). */
const START_TOLERANCE_SECONDS = 60;
/** Jaccard similarity above which a roster counts as "the same group". */
const ROSTER_SIMILARITY_THRESHOLD = 0.5;
/**
 * A world boss has a crowd on it, and what each uploader saw of it differs a lot (a client of an older version knew eight names, a later one
 * the whole thousand): Jaccard of 9 against 1107 names is below 1 %. So on a world boss it is enough that the smaller roster lies in the larger one.
 */
const WORLD_BOSS_CONTAINMENT_THRESHOLD = 0.8;
/** The smaller roster needs this many names for the containment rule - below it a couple of shared names would prove nothing. */
const WORLD_BOSS_CONTAINMENT_MIN_NAMES = 5;
/** How far a returning participant's damage may drift between uploads and still count as the same row. */
const DAMAGE_MATCH_TOLERANCE = 0.15;

export interface ProcessResult {
  status: "created" | "merged";
  encounterId: number;
  serverId: number;
}

/** Looks up the server by (fingerprint, displayName), inserting it on first sight. A real incident
 * (see schema.ts's own remarks on `servers`) confirmed the fingerprint alone can collide between
 * two genuinely different private-server operators - matching on displayName too, not overwriting
 * a differently-named row's name, is what keeps them apart instead of one silently hijacking the
 * other's row (and, with it, every one of its already-uploaded players/encounters). */
function upsertServer(rawFingerprint: string, rawDisplayName: string | undefined): number {
  const { fingerprint, displayName } = canonicalServer(rawFingerprint, rawDisplayName);
  if (displayName) {
    const exact = db
      .select()
      .from(servers)
      .where(and(eq(servers.fingerprint, fingerprint), eq(servers.displayName, displayName)))
      .get();
    if (exact) {
      return exact.id;
    }

    // A same-fingerprint row with no displayName yet (an older upload from before this field
    // existed, or a client that never reported one) is this same server still finding its name -
    // claim it rather than creating a duplicate unnamed row next to a properly named one.
    const unnamed = db
      .select()
      .from(servers)
      .where(and(eq(servers.fingerprint, fingerprint), isNull(servers.displayName)))
      .get();
    if (unnamed) {
      db.update(servers).set({ displayName }).where(eq(servers.id, unnamed.id)).run();
      return unnamed.id;
    }

    const inserted = db.insert(servers).values({ fingerprint, displayName }).run();
    return Number(inserted.lastInsertRowid);
  }

  // No displayName on this upload at all - fall back to fingerprint alone, same best-effort
  // behavior as before (genuinely ambiguous only if two different real servers share a
  // fingerprint AND this particular upload came in nameless).
  const existing = db.select().from(servers).where(eq(servers.fingerprint, fingerprint)).get();
  if (existing) {
    return existing.id;
  }

  const inserted = db.insert(servers).values({ fingerprint, displayName: null }).run();
  return Number(inserted.lastInsertRowid);
}

/** Boss rows of one game - the game lives on the instance, so this is always a join. */
function bossesOfGame(game: Game) {
  return db
    .select({ boss: bosses, instanceSlug: instances.slug })
    .from(bosses)
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .where(eq(instances.game, game))
    .all();
}

/**
 * Difficulty step encoded in a boss's NPC ids: the n-th id (ascending) of a Nightmare boss is level n. An Ascension Rite
 * boss's step is its level when the client sent it, else the table of the ids known so far (ASCENSION_STEP_BY_NPC); any other
 * id has no step. Transcendence has no
 * rule common to its instances; only the steps a player confirmed are known (TRANSCENDENCE_STEPS).
 */
export function modeFromNpcId(bossId: number, npcId: number, bossLevel?: number): string {
  const row = db
    .select({ category: instances.category, slug: instances.slug })
    .from(bosses)
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .where(eq(bosses.id, bossId))
    .get();
  if (row?.category === "transcendence") {
    return TRANSCENDENCE_STEPS[row.slug ?? ""]?.[Math.floor(npcId / 1000)] ?? "";
  }
  if (row?.category === "ascension") {
    return (bossLevel !== undefined ? ASCENSION_STEP_BY_LEVEL[bossLevel] : undefined) ?? ASCENSION_STEP_BY_NPC[npcId] ?? "";
  }
  if (row?.category !== "nightmare") {
    return "";
  }
  const ids = db.select({ npcId: bossNpcIds.npcId }).from(bossNpcIds).where(eq(bossNpcIds.bossId, bossId)).all().map((r) => r.npcId).sort((a, b) => a - b);
  const index = ids.indexOf(npcId);
  if (index < 0) {
    return "";
  }
  return String(index + 1);
}

/** The difficulty of an Ascension Rite by the boss's level (from its spawn frame). */
const ASCENSION_STEP_BY_LEVEL: Readonly<Record<number, string>> = { 45: "easy", 50: "medium", 55: "hard", 60: "extreme" };

/**
 * The step of an Ascension boss whose NPC id is known to belong to it, for clients that do not send the level. The ids are one per step, but their
 * order differs between bosses (Divided Aponos rises with the level, Featherstorm Duduri and Watchful Raptor Dodori fall), so the step is not read off
 * the rank. Confirmed by the boss's level in recorded spawn frames and by its hit points (about 590k / 740k / 1.17M / 1.7M for 45 / 50 / 55 / 60).
 */
const ASCENSION_STEP_BY_NPC: Readonly<Record<number, string>> = {
  2701753: "easy", 2701789: "medium", 2701807: "hard", // Divided Aponos
  2701853: "easy", 2701841: "medium", 2701829: "hard", 2701774: "extreme", // Featherstorm Duduri
  2701854: "easy", 2701842: "medium", 2701830: "hard", 2701788: "extreme", // Watchful Raptor Dodori
};

/**
 * Transcendence step by instance and the first four digits of the boss's NPC ids (2330709 -> 2330). Only what a player
 * confirmed: Shattered Arkanis stage 1 fights carry 2330xxx (11.07 M HP), stage 2 fights 2350xxx (13.69 M HP); Deus Research Base stage 1 fights carry 2320xxx, stage 2 fights 2340xxx (13.69 M HP on Atiel, 7.24 M on Nazmun: the same
 * 1.237 x over stage 1 as Shattered Arkanis shows, and the same +20 in the id; first seen 2026-10-11). The instances differ (Deus Research Base also has 2310 and
 * 2320), so nothing is derived from a pattern; unknown ids leave the step empty ("Stage unknown" on the boss page).
 */
const TRANSCENDENCE_STEPS: Readonly<Record<string, Readonly<Record<number, string>>>> = {
  // Shattered Arkanis stage 3 is 2353 (Siliator 2353707 and Ruthilis 2353708, 9.68 M HP each, confirmed by the player 2026-10-15; the +20 pattern guessed 2370).
  // Stage 4 is not seen yet, so it is left out rather than guessed.
  "shattered-arkanis": { 2330: "1", 2350: "2", 2353: "3" },
  // Deus Research Base stage 3 is 2343, not 2360 as the +20 pattern guessed: Gelcos 2343616 and Nazmun 2343615 (9.68 M HP each, confirmed by the player
  // 2026-10-14, 1.337 x over stage 2). Stage 4 is not seen yet, so it is left out rather than guessed.
  "deus-research-base": { 2320: "1", 2340: "2", 2343: "3" },
};

/** The boss a payload names when it is on file (by NPC id, else by name), without creating anything; null for one nobody has catalogued. */
export function findBossId(payload: UploadPayload): number | null {
  if (payload.bossNpcId !== undefined) {
    const byNpcId = db.select({ bossId: bossNpcIds.bossId }).from(bossNpcIds).where(eq(bossNpcIds.npcId, payload.bossNpcId)).get();
    if (byNpcId) {
      return byNpcId.bossId;
    }
  }

  const npcName = payload.bossNpcName;
  return bossesOfGame(payload.game).find(({ boss }) => boss.name === npcName || boss.npcNameAliases.includes(npcName))?.boss.id ?? null;
}

function resolveBossId(payload: UploadPayload): number {
  // Aion 2 clients know the NPC's numeric id, which is unambiguous where names are not (the same
  // boss name recurs across dungeons there) - so it wins whenever the boss is on file.
  if (payload.bossNpcId !== undefined) {
    const byNpcId = db.select({ bossId: bossNpcIds.bossId }).from(bossNpcIds).where(eq(bossNpcIds.npcId, payload.bossNpcId)).get();
    if (byNpcId) {
      return byNpcId.bossId;
    }
  }

  // Never across games: "Kromede" in Aion 2's Fire Temple is not the Aion boss of the same name.
  const gameBosses = bossesOfGame(payload.game);
  const npcName = payload.bossNpcName;
  const match = gameBosses.find(({ boss }) => boss.name === npcName || boss.npcNameAliases.includes(npcName));
  if (match) {
    return match.boss.id;
  }

  let unassigned = db
    .select({ id: instances.id })
    .from(instances)
    .where(and(eq(instances.name, UNASSIGNED_INSTANCE_NAME), eq(instances.game, payload.game)))
    .get();
  if (!unassigned) {
    const inserted = db
      .insert(instances)
      .values({ name: UNASSIGNED_INSTANCE_NAME, nameEn: "Unassigned", slug: "unassigned", game: payload.game, sortOrder: -1 })
      .run();
    unassigned = { id: Number(inserted.lastInsertRowid) };
  }

  const takenSlugs = new Set(gameBosses.map(({ boss }) => boss.slug).filter((s): s is string => s !== null));
  const nameEn = npcName;
  const slug = uniqueSlug(slugify(nameEn ?? npcName) || "boss", (s) => takenSlugs.has(s));
  const insertedBoss = db
    .insert(bosses)
    .values({ instanceId: unassigned.id, name: npcName, nameEn, slug, npcNameAliases: [] })
    .run();
  return Number(insertedBoss.lastInsertRowid);
}

/** The server a participant belongs to: the one he announced himself with when the client sent it (a matched world holds players of
 * several servers), else the uploader's. */
function serverOfParticipant(participant: { serverId?: number }, uploaderServerId: number): number {
  if (participant.serverId === undefined || nameOf(participant.serverId) === null) {
    return uploaderServerId;
  }
  return upsertServer(`aion2:aion-2-server-${participant.serverId}`, undefined);
}

/**
 * After an upload: a player whose own client named his server is verified by that; every other player of the upload is checked against
 * NC's character search in the background (see ncMatch.ts). Never blocks the upload.
 */
function checkUploadedPlayers(participants: { name: string; serverId?: number; profile?: { level?: number; classId?: number } }[], uploaderServerId: number) {
  try {
    const ids: number[] = [];
    const named: { id: number; level?: number; classId?: number }[] = [];
    for (const p of participants) {
      const row = db
        .select({ id: players.id })
        .from(players)
        .where(and(eq(players.serverId, serverOfParticipant(p, uploaderServerId)), eq(players.nameNormalized, normalizeName(p.name))))
        .get();
      if (!row) {
        continue;
      }
      named.push({ id: row.id, level: p.profile?.level, classId: p.profile?.classId });
      if (p.serverId !== undefined && nameOf(p.serverId) !== null) {
        noteClientServer(row.id, p.serverId);
      } else {
        ids.push(row.id);
      }
    }
    checkPlayers(ids);
    refreshAfterUpload(named);
  } catch {
    // the check is an extra; the upload is already stored
  }
}

/** Players an old client filed under the uploader's server that this upload now names with their own: see rehome.ts. */
function rehomePlayers(participants: { name: string; guild?: string; serverId?: number }[], uploaderServerId: number) {
  const named = participants
    .filter((p) => p.serverId !== undefined && nameOf(p.serverId) !== null)
    .map((p) => ({ name: p.name, guild: p.guild, target: serverOfParticipant(p, uploaderServerId) }));
  if (named.length > 0) {
    rehomeKnownPlayers(named, uploaderServerId);
  }
}

/** The faction an upload reports for a participant: his own, else that of the server he announced himself with (1xxx Elyos, 2xxx Asmodian). */
function factionOfParticipant(participant: { faction?: string; serverId?: number }): string {
  if (participant.faction) {
    return participant.faction;
  }
  const label = participant.serverId !== undefined ? nameOf(participant.serverId) : null;
  return label ? factionOfServerName(label) : "";
}

function upsertPlayer(name: string, serverId: number, guild?: string, faction?: string, characterId?: number): number {
  const id = upsertPlayerRow(name, serverId, guild, faction, characterId);
  noteGameCharacterId(id, serverId, characterId);
  return id;
}

/**
 * The game's permanent character id of a player, kept as a hint. A NEW row (a name we never saw) that comes with an id another row of the same server already holds is a renamed
 * character: the old row is asked about at once - NC knows his character id and says what he is called now (the new row is merged into the old one then, see ncMatch.applyName).
 * The id never decides anything by itself, so a made-up one costs one extra NC request.
 */
function noteGameCharacterId(playerId: number, serverId: number, characterId?: number): void {
  if (characterId === undefined) {
    return;
  }
  try {
    const mine = db.select({ id: players.gameCharacterId }).from(players).where(eq(players.id, playerId)).get();
    if (mine?.id === characterId) {
      return;
    }
    const older = db.select({ id: players.id }).from(players).where(and(eq(players.serverId, serverId), eq(players.gameCharacterId, characterId), ne(players.id, playerId))).all();
    db.update(players).set({ gameCharacterId: characterId }).where(eq(players.id, playerId)).run();
    if (older.length === 1) {
      askAboutRenamed(older[0].id);
    }
  } catch {
    // a hint only
  }
}

function upsertPlayerRow(name: string, serverId: number, guild?: string, faction?: string, characterId?: number): number {
  const nameNormalized = normalizeName(name);
  const existing = db
    .select()
    .from(players)
    .where(and(eq(players.serverId, serverId), eq(players.nameNormalized, nameNormalized)))
    .get();
  if (existing) {
    db.update(players)
      .set({ name, lastSeenAt: sql`(current_timestamp)`, ...(guild ? { guild } : {}), ...(faction ? { faction } : {}) })
      .where(eq(players.id, existing.id))
      .run();
    if (guild) {
      linkPlayerGuild(existing.id);
      reconcileGuildWithNc(existing.id);
    }
    return existing.id;
  }

  // Per the user: a renamed character otherwise gets a brand new `players` row every time its OLD
  // name resurfaces (a late/replayed upload of an older Chat.log) - see
  // players.aliasNamesNormalized's own remarks. Checked only once the direct lookup above has
  // already missed, and deliberately never touches `name`/`nameNormalized` on a match: an old
  // aliased name showing up again must not regress the player's current display name backward.
  const aliasMatch = db
    .select()
    .from(players)
    .where(eq(players.serverId, serverId))
    .all()
    .find((p) => p.aliasNamesNormalized.includes(nameNormalized));
  if (aliasMatch) {
    db.update(players)
      .set({ lastSeenAt: sql`(current_timestamp)`, ...(guild ? { guild } : {}), ...(faction ? { faction } : {}) })
      .where(eq(players.id, aliasMatch.id))
      .run();
    if (guild) {
      linkPlayerGuild(aliasMatch.id);
      reconcileGuildWithNc(aliasMatch.id);
    }
    return aliasMatch.id;
  }

  const inserted = db.insert(players).values({ name, nameNormalized, serverId, guild: guild ?? null, faction: faction ?? "" }).run();
  const playerId = Number(inserted.lastInsertRowid);
  ensurePlayerSlug(playerId);
  if (guild) {
    linkPlayerGuild(playerId);
  }
  return playerId;
}

function isWorldBossId(bossId: number): boolean {
  return (
    db
      .select({ category: instances.category })
      .from(bosses)
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(eq(bosses.id, bossId))
      .get()?.category === "worldboss"
  );
}

/** Share of the smaller roster that is in the larger one; 0 when the smaller has too few names for the rule (see WORLD_BOSS_CONTAINMENT_*). */
function containment(a: ReadonlySet<string>, b: ReadonlySet<string>): number {
  const [small, large] = a.size <= b.size ? [a, b] : [b, a];
  if (small.size < WORLD_BOSS_CONTAINMENT_MIN_NAMES) {
    return 0;
  }
  let shared = 0;
  for (const name of small) {
    if (large.has(name)) {
      shared += 1;
    }
  }
  const share = shared / small.size;
  // reported as a similarity: reaches the threshold of the roster rule only when the containment rule is met
  return share >= WORLD_BOSS_CONTAINMENT_THRESHOLD ? Math.max(share, ROSTER_SIMILARITY_THRESHOLD) : 0;
}

function findCandidateEncounter(bossId: number, serverId: number, payload: UploadPayload) {
  const startedAtMs = Date.parse(payload.startedAt);
  const endedAtMs = Date.parse(payload.endedAt);

  // serverId first: two different servers must never be candidates for the same encounter even if
  // boss, timing and roster all happen to coincide (per the user, gear standards differ completely
  // between servers, so a merge across them would be actively misleading, not just imprecise).
  const timeCandidates = db
    .select()
    .from(encounters)
    .where(and(eq(encounters.serverId, serverId), eq(encounters.bossId, bossId), eq(encounters.mode, payload.mode)))
    .all()
    .filter((e) => {
      const existingStartMs = Date.parse(e.startedAt);
      const existingEndMs = Date.parse(e.endedAt);
      return (
        Math.abs(existingStartMs - startedAtMs) <= START_TOLERANCE_SECONDS * 1000 &&
        Math.abs(existingEndMs - endedAtMs) <= TIME_TOLERANCE_SECONDS * 1000
      );
    });

  if (timeCandidates.length === 0) {
    return null;
  }

  // Own name may be wrong (see class docstring), so the primary comparison
  // uses only the teammates the uploader saw under their real names. But
  // relying on that alone fails a real, narrower case: a solo upload with no
  // visible teammates at all has an empty "others" set, which can never pass
  // a similarity threshold against anything - even a later upload of the
  // exact same fight. A second comparison that also includes the (possibly
  // wrong) self name catches that case; it can only ever ADD one name to the
  // set, so on a genuine "wrong self name" upload it just makes that one
  // comparison a bit weaker, never wrongly stronger - the without-self
  // comparison still catches that scenario on its own (see merge.test.ts).
  const uploadRosterNamesWithoutSelf = new Set(
    payload.participants.filter((p) => !p.isSelf).map((p) => normalizeName(p.name)),
  );
  const uploadRosterNamesWithSelf = new Set(
    payload.participants.map((p) => normalizeName(p.name)),
  );

  const worldBoss = isWorldBossId(bossId);
  let best: { encounterId: number; similarity: number } | null = null;
  for (const candidate of timeCandidates) {
    const existingRoster = db
      .select({ name: players.name })
      .from(encounterParticipants)
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .where(eq(encounterParticipants.encounterId, candidate.id))
      .all();
    const existingNames = new Set(existingRoster.map((r) => normalizeName(r.name)));

    const similarity = Math.max(
      jaccardSimilarity(uploadRosterNamesWithoutSelf, existingNames),
      jaccardSimilarity(uploadRosterNamesWithSelf, existingNames),
      worldBoss ? containment(uploadRosterNamesWithSelf, existingNames) : 0,
      // The same world boss on the same server at the same moment with exactly the same hit points is one kill, even when each uploader saw
      // another part of the crowd (21 and 30 names, 6 in common). Only when both know the number - a reading from a late join is lower.
      worldBoss && payload.bossMaxHp !== undefined && candidate.bossMaxHp === payload.bossMaxHp ? 1 : 0,
    );
    if (similarity >= ROSTER_SIMILARITY_THRESHOLD && (!best || similarity > best.similarity)) {
      best = { encounterId: candidate.id, similarity };
    }
  }

  return best?.encounterId ?? null;
}

/** Shared by insertParticipant/overwriteParticipant for both the damage and heal skill lists -
 * isHeal is what tells them apart in encounter_skill_usage (see schema.ts's own remarks). */
function insertSkillUsage(participantId: number, skills: ParticipantUpload["skills"], isHeal: boolean) {
  if (skills.length === 0) {
    return;
  }

  db.insert(encounterSkillUsage)
    .values(
      skills.map((s) => ({
        participantId,
        skillName: s.skill,
        hits: s.hits,
        critHits: s.critHits,
        totalDamage: s.total,
        minHit: s.min,
        maxHit: s.max,
        isHeal,
      })),
    )
    .run();
}

/** Same idea as insertSkillUsage above, but for real buffs (see encounterBuffUsage's own remarks
 * on why it's a separate table rather than another isHeal-style flag). */
function insertBuffUsage(participantId: number, buffs: ParticipantUpload["buffs"]) {
  if (buffs.length === 0) {
    return;
  }

  db.insert(encounterBuffUsage)
    .values(buffs.map((b) => ({ participantId, skillName: b.skill, casts: b.casts, skillId: b.skillId, uptimePermille: b.uptimePermille, giversJson: JSON.stringify(b.givers) })))
    .run();
}

function insertParticipant(
  encounterId: number,
  serverId: number,
  participant: ParticipantUpload,
  authoritative: boolean,
) {
  const playerId = upsertPlayer(participant.name, serverOfParticipant(participant, serverId), participant.guild, factionOfParticipant(participant), participant.characterId);
  if (participant.profile) {
    upsertProfile(playerId, participant.profile);
  }
  const inserted = db
    .insert(encounterParticipants)
    .values({
      encounterId,
      playerId,
      className: participant.className,
      faction: participant.faction,
      totalDamage: participant.totalDamage,
      dps: participant.dps,
      idps: participant.idps,
      estimated: participant.estimated,
      totalHealing: participant.totalHealing,
      hps: participant.hps,
      damageTaken: participant.damageTaken,
      damageAbsorbed: participant.damageAbsorbed,
      shieldsGiven: participant.shieldsGiven,
      critRatePercent: critRateOf(participant),
      isCritRateAuthoritative: authoritative,
    })
    .run();
  const participantId = Number(inserted.lastInsertRowid);

  insertSkillUsage(participantId, participant.skills, false);
  insertSkillUsage(participantId, participant.healSkills, true);
  insertBuffUsage(participantId, participant.buffs);
}

function critRateOf(participant: ParticipantUpload): number {
  const totalHits = participant.skills.reduce((sum, s) => sum + s.hits, 0);
  const totalCrits = participant.skills.reduce((sum, s) => sum + s.critHits, 0);
  return totalHits > 0 ? (100 * totalCrits) / totalHits : 0;
}

function overwriteParticipant(participantId: number, participant: ParticipantUpload, authoritative: boolean) {
  db.update(encounterParticipants)
    .set({
      className: participant.className,
      faction: participant.faction,
      totalDamage: participant.totalDamage,
      dps: participant.dps,
      idps: participant.idps,
      estimated: participant.estimated,
      totalHealing: participant.totalHealing,
      hps: participant.hps,
      damageTaken: participant.damageTaken,
      damageAbsorbed: participant.damageAbsorbed,
      shieldsGiven: participant.shieldsGiven,
      critRatePercent: critRateOf(participant),
      isCritRateAuthoritative: authoritative,
    })
    .where(eq(encounterParticipants.id, participantId))
    .run();

  db.delete(encounterSkillUsage).where(eq(encounterSkillUsage.participantId, participantId)).run();
  insertSkillUsage(participantId, participant.skills, false);
  insertSkillUsage(participantId, participant.healSkills, true);

  // A client that sends no buffs (an older one) must not wipe what another upload of the same fight already brought.
  if (participant.buffs.length > 0) {
    db.delete(encounterBuffUsage).where(eq(encounterBuffUsage.participantId, participantId)).run();
    insertBuffUsage(participantId, participant.buffs);
  }
}

function recomputeEncounterTotals(encounterId: number, startedAtIso: string, endedAtIso: string) {
  const durationSeconds = Math.max(
    0.001,
    (Date.parse(endedAtIso) - Date.parse(startedAtIso)) / 1000,
  );
  const participantRows = db
    .select({ totalDamage: encounterParticipants.totalDamage })
    .from(encounterParticipants)
    .where(eq(encounterParticipants.encounterId, encounterId))
    .all();
  const totalDamage = participantRows.reduce((sum, p) => sum + p.totalDamage, 0);

  const rosterRows = db
    .select({ name: players.name })
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .where(eq(encounterParticipants.encounterId, encounterId))
    .all();
  const rosterFingerprint = rosterRows
    .map((r) => normalizeName(r.name))
    .sort()
    .join(",");

  db.update(encounters)
    .set({
      startedAt: startedAtIso,
      endedAt: endedAtIso,
      durationSeconds,
      groupIDps: totalDamage / durationSeconds,
      rosterFingerprint,
    })
    .where(eq(encounters.id, encounterId))
    .run();
}

/** Merges a new upload's participants into an already-matched encounter, correcting a wrong self-name where possible. */
function mergeIntoEncounter(encounterId: number, serverId: number, payload: UploadPayload) {
  const existing = db.select().from(encounters).where(eq(encounters.id, encounterId)).get()!;
  if (payload.bossMaxHp !== undefined && payload.bossMaxHp > (existing.bossMaxHp ?? 0)) {
    // Every client saw the same boss; the highest reading is the closest to its real maximum.
    db.update(encounters).set({ bossMaxHp: payload.bossMaxHp }).where(eq(encounters.id, encounterId)).run();
  }
  const existingParticipants = db
    .select()
    .from(encounterParticipants)
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .where(eq(encounterParticipants.encounterId, encounterId))
    .all();

  for (const participant of payload.participants) {
    if (!participant.isSelf) {
      // A teammate's numbers are the same objective fact in every log that
      // saw them (damage dealt is broadcast, not viewer-dependent) - only
      // update non-authoritative rows, never overwrite a self-report.
      const normalized = normalizeName(participant.name);
      const match = existingParticipants.find(
        (p) => normalizeName(p.players.name) === normalized,
      );
      if (match && participant.estimated && !match.encounter_participants.estimated) {
        // An exact row stays: an estimate (hit counts x typical hit) never replaces real numbers.
        continue;
      }
      if (match && match.encounter_participants.estimated && !participant.estimated) {
        // Real numbers replace an estimate (damage and skill rows), whoever reported the estimate.
        overwriteParticipant(match.encounter_participants.id, participant, match.encounter_participants.isCritRateAuthoritative);
      } else if (match && !match.encounter_participants.isCritRateAuthoritative) {
        overwriteParticipant(match.encounter_participants.id, participant, false);
      } else if (!match) {
        insertParticipant(encounterId, serverId, participant, false);
      }
      continue;
    }

    // The uploader's own row - the one that might carry a wrong self-name.
    const normalized = normalizeName(participant.name);
    const sameNameMatch = existingParticipants.find(
      (p) => normalizeName(p.players.name) === normalized,
    );
    if (sameNameMatch && participant.estimated && !sameNameMatch.encounter_participants.estimated) {
      continue;
    }
    if (sameNameMatch && sameNameMatch.encounter_participants.estimated && !participant.estimated) {
      // The estimate has nothing to compare against (class aside): real numbers simply replace it.
      overwriteParticipant(sameNameMatch.encounter_participants.id, participant, true);
      continue;
    }
    if (sameNameMatch) {
      if (
        sameNameMatch.encounter_participants.className === participant.className &&
        withinRelativeTolerance(
          sameNameMatch.encounter_participants.totalDamage,
          participant.totalDamage,
          DAMAGE_MATCH_TOLERANCE,
        )
      ) {
        // Name already agrees with what others reported - simple authoritative overwrite.
        overwriteParticipant(sameNameMatch.encounter_participants.id, participant, true);
      } else {
        // Same name already in the roster, but class/damage disagree - a real
        // data conflict, not a naming problem. Adding a new row is safer than
        // guessing which of two contradictory reports to keep, or reassigning
        // some OTHER, unrelated row's identity to resolve it.
        insertParticipant(encounterId, serverId, participant, true);
      }
      continue;
    }

    // No row uses this name yet - look for an existing, not-yet-authoritative
    // row (i.e. nobody has self-reported for it) whose class and damage line up
    // with what this upload claims for itself. Renaming it corrects the case
    // where every uploader's own configured name was wrong.
    const candidates = existingParticipants.filter(
      (p) =>
        !p.encounter_participants.isCritRateAuthoritative &&
        p.encounter_participants.className === participant.className &&
        withinRelativeTolerance(
          p.encounter_participants.totalDamage,
          participant.totalDamage,
          DAMAGE_MATCH_TOLERANCE,
        ),
    );

    if (candidates.length === 1) {
      const playerId = upsertPlayer(participant.name, serverOfParticipant(participant, serverId), participant.guild, factionOfParticipant(participant), participant.characterId);
      if (participant.profile) {
        upsertProfile(playerId, participant.profile);
      }
      db.update(encounterParticipants)
        .set({ playerId })
        .where(eq(encounterParticipants.id, candidates[0].encounter_participants.id))
        .run();
      overwriteParticipant(candidates[0].encounter_participants.id, participant, true);
    } else {
      // Ambiguous or no match at all - safer to add a new row than to guess
      // wrong and silently rename the wrong person.
      insertParticipant(encounterId, serverId, participant, true);
    }
  }

  const newStartedAt = new Date(
    Math.min(Date.parse(existing.startedAt), Date.parse(payload.startedAt)),
  ).toISOString();
  const newEndedAt = new Date(
    Math.max(Date.parse(existing.endedAt), Date.parse(payload.endedAt)),
  ).toISOString();

  db.update(encounters)
    .set({ mergedUploadCount: existing.mergedUploadCount + 1 })
    .where(eq(encounters.id, encounterId))
    .run();
  recomputeEncounterTotals(encounterId, newStartedAt, newEndedAt);
}

function createEncounter(bossId: number, serverId: number, payload: UploadPayload): number {
  const inserted = db
    .insert(encounters)
    .values({
      serverId,
      bossId,
      startedAt: payload.startedAt,
      endedAt: payload.endedAt,
      durationSeconds: 0,
      groupIDps: 0,
      rosterFingerprint: "",
      mergedUploadCount: 1,
      mode: payload.mode,
      bossMaxHp: payload.bossMaxHp ?? null,
    })
    .run();
  const encounterId = Number(inserted.lastInsertRowid);

  for (const participant of payload.participants) {
    insertParticipant(encounterId, serverId, participant, participant.isSelf);
  }

  recomputeEncounterTotals(encounterId, payload.startedAt, payload.endedAt);
  return encounterId;
}

/** Sums a and b's skill/heal-skill/buff lists by matching skill name - the shared merge step for
 * both mergeDuplicateParticipants below and any future spot that needs to combine two of these
 * lists (min/max widen to cover both sides, everything else adds). */
function mergeSkillLists<T extends { skill: string; hits?: number; critHits?: number; total?: number; min?: number; max?: number; casts?: number }>(
  a: T[],
  b: T[],
): T[] {
  const bySkill = new Map<string, T>();
  for (const s of [...a, ...b]) {
    const existing = bySkill.get(s.skill);
    if (!existing) {
      bySkill.set(s.skill, { ...s });
      continue;
    }

    bySkill.set(s.skill, {
      ...existing,
      hits: (existing.hits ?? 0) + (s.hits ?? 0),
      critHits: (existing.critHits ?? 0) + (s.critHits ?? 0),
      total: (existing.total ?? 0) + (s.total ?? 0),
      min: existing.min !== undefined && s.min !== undefined ? Math.min(existing.min, s.min) : (existing.min ?? s.min),
      max: existing.max !== undefined && s.max !== undefined ? Math.max(existing.max, s.max) : (existing.max ?? s.max),
      casts: (existing.casts ?? 0) + (s.casts ?? 0),
    } as T);
  }

  return [...bySkill.values()];
}

/** Two buff lists of one person (a renamed character's two rows): the counts add up, the uptime is the longer of the two (the same fight, seen twice), the givers are united. */
export function mergeBuffLists(a: ParticipantUpload["buffs"], b: ParticipantUpload["buffs"]): ParticipantUpload["buffs"] {
  const bySkill = new Map<string, ParticipantUpload["buffs"][number]>();
  for (const buff of [...a, ...b]) {
    const existing = bySkill.get(buff.skill);
    if (!existing) {
      bySkill.set(buff.skill, { ...buff, givers: [...buff.givers] });
      continue;
    }

    bySkill.set(buff.skill, {
      skill: existing.skill,
      casts: existing.casts + buff.casts,
      skillId: existing.skillId || buff.skillId,
      uptimePermille: Math.max(existing.uptimePermille, buff.uptimePermille),
      givers: [...new Set([...existing.givers, ...buff.givers])].slice(0, 8),
    });
  }

  return [...bySkill.values()];
}

/**
 * Per the user: a real character rename otherwise splits one person across two participant rows
 * in the SAME upload forever, since the client's own Chat.log parsing has no way to know
 * "Alhamdulilah" and "Hidan" are the same real person - only a manually curated alias (see
 * players.aliasNamesNormalized) can say so. Runs once, before anything else touches
 * payload.participants, so every downstream step (self-participant count check, boss/roster
 * matching, participant insertion) only ever sees one row per real person. Two participants
 * collapse into one only when they resolve to the very same existing `players` row - two
 * genuinely different people who happen to share a normalized name on two different servers are
 * unaffected, since aliases are looked up within one specific serverId.
 */
function mergeDuplicateParticipants(participants: ParticipantUpload[], serverId: number): ParticipantUpload[] {
  const allOnServer = db.select().from(players).where(eq(players.serverId, serverId)).all();
  const canonicalIdOf = new Map<string, number>();
  for (const p of allOnServer) {
    canonicalIdOf.set(p.nameNormalized, p.id);
    for (const alias of p.aliasNamesNormalized) {
      canonicalIdOf.set(alias, p.id);
    }
  }

  // Group by canonical player id when one is known, otherwise by the reported name itself (a
  // brand new player this upload is introducing has no `players` row yet to key on).
  const groups = new Map<string | number, ParticipantUpload[]>();
  for (const participant of participants) {
    const normalized = normalizeName(participant.name);
    const key = canonicalIdOf.get(normalized) ?? normalized;
    const group = groups.get(key) ?? [];
    group.push(participant);
    groups.set(key, group);
  }

  return [...groups.values()].map((group) => {
    if (group.length === 1) {
      return group[0];
    }

    // The isSelf row (if any) wins for name/className/faction - it's the uploader's own
    // authoritative report of themselves, the same trust order insertParticipant already gives
    // a self-report over a third-person one.
    const primary = group.find((p) => p.isSelf) ?? group[0];
    return {
      ...primary,
      isSelf: group.some((p) => p.isSelf),
      totalDamage: group.reduce((sum, p) => sum + p.totalDamage, 0),
      // One estimated half makes the summed damage an estimate.
      estimated: group.some((p) => p.estimated),
      totalHealing: group.reduce((sum, p) => sum + p.totalHealing, 0),
      // idps/dps are already rates, not sums-of-rates - re-derived from the summed totals divided
      // by whichever single row's own rate implies the longest window, so a merge never invents a
      // faster clip than either half actually sustained on its own.
      dps: Math.max(...group.map((p) => p.dps)),
      idps: Math.max(...group.map((p) => p.idps)),
      damageTaken: group.reduce((sum, p) => sum + p.damageTaken, 0),
      damageAbsorbed: group.reduce((sum, p) => sum + p.damageAbsorbed, 0),
      shieldsGiven: [...group.flatMap((p) => p.shieldsGiven).reduce((acc, g) => acc.set(g.playerName, (acc.get(g.playerName) ?? 0) + g.amount), new Map<string, number>())].map(([playerName, amount]) => ({ playerName, amount })),
      skills: group.reduce((acc, p) => mergeSkillLists(acc, p.skills), [] as ParticipantUpload["skills"]),
      healSkills: group.reduce((acc, p) => mergeSkillLists(acc, p.healSkills), [] as ParticipantUpload["healSkills"]),
      buffs: group.reduce((acc, p) => mergeBuffLists(acc, p.buffs), [] as ParticipantUpload["buffs"]),
    };
  });
}

/**
 * `receivedAt` is the moment the upload reached the server, in the database's own text format. An upload that waited for the NC check or
 * a review is only merged later, but the fight must show up (feed, "x minutes ago") at the time it was really uploaded, so the encounter
 * is never dated later than its earliest upload.
 */
export function processUpload(payload: UploadPayload, receivedAt?: string): ProcessResult {
  // One transaction for the whole upload: the dozens of single inserts commit (and sync to disk) once instead of one by one, and a
  // failure half way leaves no half-written fight behind.
  return sqlite.transaction((): ProcessResult => {
    const result = processUploadAt(payload);
    if (receivedAt) {
      db.update(encounters)
        .set({ createdAt: sql`min(${encounters.createdAt}, ${receivedAt})` })
        .where(eq(encounters.id, result.encounterId))
        .run();
    }
    return result;
  })();
}

function processUploadAt(payload: UploadPayload): ProcessResult {
  const serverId = upsertServer(payload.serverFingerprint, payload.serverName);
  rehomePlayers(payload.participants, serverId);
  payload = { ...payload, participants: mergeDuplicateParticipants(payload.participants, serverId) };
  const bossId = resolveBossId(payload);
  // Nightmare bosses carry one NPC id per level (Pinopi 2980040 = level 1 ... 2980049 = level 10), so
  // the level is read off the id when the client did not name it.
  if (payload.mode === "" && payload.bossNpcId !== undefined) {
    payload = { ...payload, mode: modeFromNpcId(bossId, payload.bossNpcId, payload.bossLevel) };
  }
  const candidateEncounterId = findCandidateEncounter(bossId, serverId, payload);

  if (candidateEncounterId) {
    mergeIntoEncounter(candidateEncounterId, serverId, payload);
    checkUploadedPlayers(payload.participants, serverId);
    return { status: "merged", encounterId: candidateEncounterId, serverId };
  }

  const encounterId = createEncounter(bossId, serverId, payload);
  checkUploadedPlayers(payload.participants, serverId);
  return { status: "created", encounterId, serverId };
}

/**
 * Files the players of a fight upload (server, name, faction, legion) without storing the fight, and queues their check against NC. For
 * the NC gate (see ncGate.ts), which has to know the players' state before the run may count. Returns the player rows in roster order.
 */
export function registerUploadPlayers(payload: UploadPayload): number[] {
  const serverId = upsertServer(payload.serverFingerprint, payload.serverName);
  rehomePlayers(payload.participants, serverId);
  const ids = payload.participants.map((p) => upsertPlayer(p.name, serverOfParticipant(p, serverId), p.guild, factionOfParticipant(p), p.characterId));
  payload.participants.forEach((p, i) => noteClassHint(ids[i], p.className));
  checkUploadedPlayers(payload.participants, serverId);
  return ids;
}

/**
 * Stores the profiles of an upload that has no boss fight (see profilesUploadSchema): the server and
 * the players are found or created like for a fight, then each profile goes through upsertProfile
 * (a "seen" profile never replaces a "self" one there).
 */
export function processProfilesUpload(payload: ProfilesUploadPayload): { serverId: number; players: number } {
  return sqlite.transaction(() => {
    const serverId = upsertServer(payload.serverFingerprint, payload.serverName);
    rehomePlayers(payload.participants, serverId);
    for (const participant of payload.participants) {
      const playerId = upsertPlayer(participant.name, serverOfParticipant(participant, serverId), participant.guild, factionOfParticipant(participant), participant.characterId);
      upsertProfile(playerId, participant.profile);
    }
    checkUploadedPlayers(payload.participants, serverId);
    return { serverId, players: payload.participants.length };
  })();
}
