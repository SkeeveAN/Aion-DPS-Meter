import { asc, desc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, encounterSkillUsage, instances, players, servers, uploads } from "../db/schema.js";
import { resolveSkillIcon } from "../skills/skillIconResolver.js";
import { topBuffsByParticipant } from "../skills/topBuffs.js";
import { gameFromQuery } from "./instances.js";

// One specific fight's full group roster (mirrors myaion.eu's PvESession) - reachable from the
// leaderboard's "top groups" list so a run can be linked to directly, not just expanded inline.
export async function encounterRoutes(app: FastifyInstance) {
  // Homepage "recent activity" feed - the most recently merged real fights, newest first, each
  // with its top damage dealer as the row's "face" (same representative-picking idea as
  // bosses.ts's topGroups, just one person instead of a whole roster). Never a fabricated event -
  // an empty game just returns an empty list.
  app.get<{ Querystring: { game?: string; limit?: string } }>("/api/activity/recent", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const limit = Math.min(Math.max(Number(request.query.limit ?? 8) || 8, 1), 20);

    const rows = db
      .select({
        encounterId: encounters.id,
        createdAt: encounters.createdAt,
        groupIDps: encounters.groupIDps,
        durationSeconds: encounters.durationSeconds,
        bossName: bosses.name,
        bossNameEn: bosses.nameEn,
        bossSlug: bosses.slug,
        instanceName: instances.name,
        instanceNameEn: instances.nameEn,
        instanceSlug: instances.slug,
      })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(eq(instances.game, game))
      .orderBy(desc(encounters.createdAt))
      .limit(limit)
      .all();

    const withTopPlayer = rows.map((row) => {
      const top = db
        .select({ playerName: players.name, className: encounterParticipants.className })
        .from(encounterParticipants)
        .innerJoin(players, eq(encounterParticipants.playerId, players.id))
        .where(eq(encounterParticipants.encounterId, row.encounterId))
        .orderBy(desc(encounterParticipants.totalDamage))
        .get();
      return { ...row, topPlayerName: top?.playerName ?? null, topPlayerClassName: top?.className ?? null };
    });

    return reply.send(withTopPlayer);
  });

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
        serverName: servers.displayName,
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
      .leftJoin(servers, eq(players.serverId, servers.id))
      .where(eq(encounterParticipants.encounterId, encounterId))
      .orderBy(desc(encounterParticipants.totalDamage))
      .all();

    const topBuffs = topBuffsByParticipant(roster.map((r) => r.participantId));
    const rosterWithBuffs = roster.map((r) => ({ ...r, topBuffs: topBuffs.get(r.participantId) ?? [] }));

    return reply.send({
      encounter: { ...encounter, appVersion: latestUpload?.clientVersion ?? null },
      roster: rosterWithBuffs,
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
        serverName: servers.displayName,
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
