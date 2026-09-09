import { asc, desc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, encounterSkillUsage, players } from "../db/schema.js";
import { resolveSkillIcon } from "../skills/skillIconResolver.js";

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
        critRatePercent: encounterParticipants.critRatePercent,
      })
      .from(encounterParticipants)
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .where(eq(encounterParticipants.encounterId, encounterId))
      .orderBy(desc(encounterParticipants.totalDamage))
      .all();

    return reply.send({ encounter, roster });
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
