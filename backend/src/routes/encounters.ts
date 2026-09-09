import { asc, desc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, encounterSkillUsage, players, uploads } from "../db/schema.js";
import { resolveSkillIcon } from "../skills/skillIconResolver.js";
import { topSkillsByParticipant } from "../skills/topSkills.js";

// One specific fight's full group roster (mirrors myaion.eu's PvESession) - reachable from the
// leaderboard's "top groups" list so a run can be linked to directly, not just expanded inline.
export async function encounterRoutes(app: FastifyInstance) {
  app.get<{ Params: { id: string } }>("/api/encounters/:id", async (request, reply) => {
    const encounterId = Number(request.params.id);
    if (!Number.isInteger(encounterId)) {
      return reply.status(400).send({ error: "invalid_encounter_id" });
    }

    const encounter = db
      .select({
        id: encounters.id,
        bossId: encounters.bossId,
        bossName: bosses.name,
        lootRules: bosses.lootRules,
        startedAt: encounters.startedAt,
        endedAt: encounters.endedAt,
        durationSeconds: encounters.durationSeconds,
        groupIDps: encounters.groupIDps,
        mergedUploadCount: encounters.mergedUploadCount,
      })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .where(eq(encounters.id, encounterId))
      .get();
    if (!encounter) {
      return reply.status(404).send({ error: "encounter_not_found" });
    }

    // An encounter is merged from however many group members' own uploads (see
    // matching/merge.ts) - there is no single "the" client version for it, only whichever build
    // sent the most recent one, which is what actually matters for "is this run's data trustworthy
    // under the latest fixes" (see e.g. the totalDamage-scoping bug fixed in 0.7.13).
    const latestUpload = db
      .select({ clientVersion: uploads.clientVersion })
      .from(uploads)
      .where(eq(uploads.matchedEncounterId, encounterId))
      .orderBy(desc(uploads.receivedAt))
      .get();

    const roster = db
      .select({
        participantId: encounterParticipants.id,
        playerId: encounterParticipants.playerId,
        playerName: players.name,
        className: encounterParticipants.className,
        faction: encounterParticipants.faction,
        totalDamage: encounterParticipants.totalDamage,
        dps: encounterParticipants.dps,
        idps: encounterParticipants.idps,
        totalHealing: encounterParticipants.totalHealing,
        hps: encounterParticipants.hps,
        damageTaken: encounterParticipants.damageTaken,
        critRatePercent: encounterParticipants.critRatePercent,
      })
      .from(encounterParticipants)
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .where(eq(encounterParticipants.encounterId, encounterId))
      .orderBy(desc(encounterParticipants.totalDamage))
      .all();

    const topSkills = topSkillsByParticipant(roster.map((r) => r.participantId));
    const rosterWithSkills = roster.map((r) => ({ ...r, topSkills: topSkills.get(r.participantId) ?? [] }));

    return reply.send({
      encounter: { ...encounter, appVersion: latestUpload?.clientVersion ?? null },
      roster: rosterWithSkills,
    });
  });

  // One player's skill breakdown for one specific fight (mirrors myaion.eu's PvEPlayerSession).
  app.get<{ Params: { id: string } }>("/api/participants/:id", async (request, reply) => {
    const participantId = Number(request.params.id);
    if (!Number.isInteger(participantId)) {
      return reply.status(400).send({ error: "invalid_participant_id" });
    }

    const participant = db
      .select({
        id: encounterParticipants.id,
        encounterId: encounterParticipants.encounterId,
        playerId: encounterParticipants.playerId,
        playerName: players.name,
        className: encounterParticipants.className,
        faction: encounterParticipants.faction,
        totalDamage: encounterParticipants.totalDamage,
        dps: encounterParticipants.dps,
        idps: encounterParticipants.idps,
        totalHealing: encounterParticipants.totalHealing,
        hps: encounterParticipants.hps,
        critRatePercent: encounterParticipants.critRatePercent,
      })
      .from(encounterParticipants)
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .where(eq(encounterParticipants.id, participantId))
      .get();
    if (!participant) {
      return reply.status(404).send({ error: "participant_not_found" });
    }

    const encounter = db
      .select({
        id: encounters.id,
        bossId: encounters.bossId,
        bossName: bosses.name,
        startedAt: encounters.startedAt,
        durationSeconds: encounters.durationSeconds,
      })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .where(eq(encounters.id, participant.encounterId))
      .get();

    const skills = db
      .select({
        skillName: encounterSkillUsage.skillName,
        hits: encounterSkillUsage.hits,
        critHits: encounterSkillUsage.critHits,
        totalDamage: encounterSkillUsage.totalDamage,
        minHit: encounterSkillUsage.minHit,
        maxHit: encounterSkillUsage.maxHit,
        isHeal: encounterSkillUsage.isHeal,
      })
      .from(encounterSkillUsage)
      .where(eq(encounterSkillUsage.participantId, participantId))
      .orderBy(asc(encounterSkillUsage.isHeal), desc(encounterSkillUsage.totalDamage))
      .all();

    const withIcons = skills.map((s) => ({ ...s, icon: resolveSkillIcon(s.skillName) }));

    return reply.send({
      participant,
      encounter,
      damageSkills: withIcons.filter((s) => !s.isHeal),
      healSkills: withIcons.filter((s) => s.isHeal),
    });
  });
}
