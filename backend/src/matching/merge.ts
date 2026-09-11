import { and, eq, sql } from "drizzle-orm";
import { db } from "../db/client.js";
import {
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
import type { ParticipantUpload, UploadPayload } from "../uploadSchema.js";
import { UNASSIGNED_INSTANCE_NAME } from "../constants.js";

/** Time-window tolerance for two encounters to even be considered the same fight. */
const TIME_TOLERANCE_SECONDS = 20;
/** Jaccard similarity above which a roster counts as "the same group". */
const ROSTER_SIMILARITY_THRESHOLD = 0.5;
/** How far a returning participant's damage may drift between uploads and still count as the same row. */
const DAMAGE_MATCH_TOLERANCE = 0.15;

export interface ProcessResult {
  status: "created" | "merged";
  encounterId: number;
  serverId: number;
}

/** Looks up the server by fingerprint, inserting it on first sight; a later, non-empty displayName
 * always overwrites an earlier one (same "latest upload wins" rule as upsertPlayer's name). */
function upsertServer(fingerprint: string, displayName: string | undefined): number {
  const existing = db.select().from(servers).where(eq(servers.fingerprint, fingerprint)).get();
  if (existing) {
    if (displayName) {
      db.update(servers).set({ displayName }).where(eq(servers.id, existing.id)).run();
    }
    return existing.id;
  }

  const inserted = db.insert(servers).values({ fingerprint, displayName: displayName ?? null }).run();
  return Number(inserted.lastInsertRowid);
}

function resolveBossId(npcName: string): number {
  const allBosses = db.select().from(bosses).all();
  const match = allBosses.find(
    (b) => b.name === npcName || b.npcNameAliases.includes(npcName),
  );
  if (match) {
    return match.id;
  }

  let unassigned = db
    .select()
    .from(instances)
    .where(eq(instances.name, UNASSIGNED_INSTANCE_NAME))
    .get();
  if (!unassigned) {
    const inserted = db
      .insert(instances)
      .values({ name: UNASSIGNED_INSTANCE_NAME, sortOrder: -1 })
      .run();
    unassigned = { id: Number(inserted.lastInsertRowid), name: UNASSIGNED_INSTANCE_NAME, sortOrder: -1, createdAt: "" };
  }

  const insertedBoss = db
    .insert(bosses)
    .values({ instanceId: unassigned.id, name: npcName, npcNameAliases: [] })
    .run();
  return Number(insertedBoss.lastInsertRowid);
}

function upsertPlayer(name: string, serverId: number): number {
  const nameNormalized = normalizeName(name);
  const existing = db
    .select()
    .from(players)
    .where(and(eq(players.serverId, serverId), eq(players.nameNormalized, nameNormalized)))
    .get();
  if (existing) {
    db.update(players)
      .set({ name, lastSeenAt: sql`(current_timestamp)` })
      .where(eq(players.id, existing.id))
      .run();
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
    db.update(players).set({ lastSeenAt: sql`(current_timestamp)` }).where(eq(players.id, aliasMatch.id)).run();
    return aliasMatch.id;
  }

  const inserted = db.insert(players).values({ name, nameNormalized, serverId }).run();
  return Number(inserted.lastInsertRowid);
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
    .where(and(eq(encounters.serverId, serverId), eq(encounters.bossId, bossId)))
    .all()
    .filter((e) => {
      const existingStartMs = Date.parse(e.startedAt);
      const existingEndMs = Date.parse(e.endedAt);
      return (
        Math.abs(existingStartMs - startedAtMs) <= TIME_TOLERANCE_SECONDS * 1000 &&
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
    .values(buffs.map((b) => ({ participantId, skillName: b.skill, casts: b.casts })))
    .run();
}

function insertParticipant(
  encounterId: number,
  serverId: number,
  participant: ParticipantUpload,
  authoritative: boolean,
) {
  const playerId = upsertPlayer(participant.name, serverId);
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
      totalHealing: participant.totalHealing,
      hps: participant.hps,
      damageTaken: participant.damageTaken,
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
      totalHealing: participant.totalHealing,
      hps: participant.hps,
      damageTaken: participant.damageTaken,
      critRatePercent: critRateOf(participant),
      isCritRateAuthoritative: authoritative,
    })
    .where(eq(encounterParticipants.id, participantId))
    .run();

  db.delete(encounterSkillUsage).where(eq(encounterSkillUsage.participantId, participantId)).run();
  insertSkillUsage(participantId, participant.skills, false);
  insertSkillUsage(participantId, participant.healSkills, true);

  db.delete(encounterBuffUsage).where(eq(encounterBuffUsage.participantId, participantId)).run();
  insertBuffUsage(participantId, participant.buffs);
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
      if (match && !match.encounter_participants.isCritRateAuthoritative) {
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
      const playerId = upsertPlayer(participant.name, serverId);
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
      totalHealing: group.reduce((sum, p) => sum + p.totalHealing, 0),
      // idps/dps are already rates, not sums-of-rates - re-derived from the summed totals divided
      // by whichever single row's own rate implies the longest window, so a merge never invents a
      // faster clip than either half actually sustained on its own.
      dps: Math.max(...group.map((p) => p.dps)),
      idps: Math.max(...group.map((p) => p.idps)),
      damageTaken: group.reduce((sum, p) => sum + p.damageTaken, 0),
      skills: group.reduce((acc, p) => mergeSkillLists(acc, p.skills), [] as ParticipantUpload["skills"]),
      healSkills: group.reduce((acc, p) => mergeSkillLists(acc, p.healSkills), [] as ParticipantUpload["healSkills"]),
      buffs: group.reduce((acc, p) => mergeSkillLists(acc, p.buffs), [] as ParticipantUpload["buffs"]),
    };
  });
}

export function processUpload(payload: UploadPayload): ProcessResult {
  const serverId = upsertServer(payload.serverFingerprint, payload.serverName);
  payload = { ...payload, participants: mergeDuplicateParticipants(payload.participants, serverId) };
  const bossId = resolveBossId(payload.bossNpcName);
  const candidateEncounterId = findCandidateEncounter(bossId, serverId, payload);

  if (candidateEncounterId) {
    mergeIntoEncounter(candidateEncounterId, serverId, payload);
    return { status: "merged", encounterId: candidateEncounterId, serverId };
  }

  const encounterId = createEncounter(bossId, serverId, payload);
  return { status: "created", encounterId, serverId };
}
