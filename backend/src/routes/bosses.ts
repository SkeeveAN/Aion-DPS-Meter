import { and, desc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, players } from "../db/schema.js";
import { topSkillsByParticipant } from "../skills/topSkills.js";

const TOP_N = 10;

export async function bossRoutes(app: FastifyInstance) {
  // serverId is required, not optional-with-a-default: per the user, different servers' gear
  // standards are incomparable, so there is no sane "all servers" leaderboard to fall back to --
  // the frontend must always ask for one specific server (see GET /api/servers for the list).
  app.get<{ Params: { id: string }; Querystring: { serverId?: string } }>(
    "/api/bosses/:id/leaderboard",
    async (request, reply) => {
      const bossId = Number(request.params.id);
      if (!Number.isInteger(bossId)) {
        return reply.status(400).send({ error: "invalid_boss_id" });
      }

      const serverId = Number(request.query.serverId);
      if (!Number.isInteger(serverId)) {
        return reply.status(400).send({ error: "missing_or_invalid_server_id" });
      }

      const boss = db.select().from(bosses).where(eq(bosses.id, bossId)).get();
      if (!boss) {
        return reply.status(404).send({ error: "boss_not_found" });
      }

      const bossResponse = { id: boss.id, name: boss.name, isSolo: boss.isSolo, lootRules: boss.lootRules };

      // Per the user: a real group fight and a solo practice target (e.g. Training Dummy) rank
      // completely differently - one boss is never both, so only the query the page actually
      // needs runs. isSolo is manually curated (see README), same pattern as isTrashMob.
      if (boss.isSolo) {
        // "Top 10 per class" has no simple SQL form worth fighting for at this
        // scale (at most a few thousand participant rows per boss) - fetch once,
        // group and cap in JS.
        const allParticipants = db
          .select({
            participantId: encounterParticipants.id,
            encounterId: encounterParticipants.encounterId,
            playerName: players.name,
            className: encounterParticipants.className,
            faction: encounterParticipants.faction,
            idps: encounterParticipants.idps,
            totalDamage: encounterParticipants.totalDamage,
            totalHealing: encounterParticipants.totalHealing,
          })
          .from(encounterParticipants)
          .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
          .innerJoin(players, eq(encounterParticipants.playerId, players.id))
          .where(and(eq(encounters.bossId, bossId), eq(encounters.serverId, serverId)))
          .all();

        const byClass = new Map<string, typeof allParticipants>();
        for (const p of allParticipants) {
          const list = byClass.get(p.className) ?? [];
          list.push(p);
          byClass.set(p.className, list);
        }

        const topByClass: Record<string, typeof allParticipants> = {};
        for (const [className, list] of byClass) {
          topByClass[className] = list.sort((a, b) => b.idps - a.idps).slice(0, TOP_N);
        }

        const topParticipantIds = Object.values(topByClass)
          .flat()
          .map((p) => p.participantId);
        const topSkills = topSkillsByParticipant(topParticipantIds);
        for (const list of Object.values(topByClass)) {
          for (const p of list as any[]) {
            p.topSkills = topSkills.get(p.participantId) ?? [];
          }
        }

        return reply.send({ boss: bossResponse, topGroups: [], topByClass });
      }

      const topGroups = db
        .select({
          encounterId: encounters.id,
          startedAt: encounters.startedAt,
          durationSeconds: encounters.durationSeconds,
          groupIDps: encounters.groupIDps,
          mergedUploadCount: encounters.mergedUploadCount,
        })
        .from(encounters)
        .where(and(eq(encounters.bossId, bossId), eq(encounters.serverId, serverId)))
        .orderBy(desc(encounters.groupIDps))
        .limit(TOP_N)
        .all();

      // The representative shown on a group's row - highest damage dealer, same "who's the face
      // of this run" choice as picking roster[0] once sorted by damage, per participant, below.
      const groupsWithRepresentative = topGroups.map((group) => {
        const roster = db
          .select({
            participantId: encounterParticipants.id,
            playerName: players.name,
            className: encounterParticipants.className,
            faction: encounterParticipants.faction,
            totalDamage: encounterParticipants.totalDamage,
            totalHealing: encounterParticipants.totalHealing,
          })
          .from(encounterParticipants)
          .innerJoin(players, eq(encounterParticipants.playerId, players.id))
          .where(eq(encounterParticipants.encounterId, group.encounterId))
          .orderBy(desc(encounterParticipants.totalDamage))
          .all();

        const totalDamage = roster.reduce((sum, p) => sum + p.totalDamage, 0);
        const totalHealing = roster.reduce((sum, p) => sum + p.totalHealing, 0);
        const representative = roster[0] ?? null;

        return {
          encounterId: group.encounterId,
          startedAt: group.startedAt,
          durationSeconds: group.durationSeconds,
          groupIDps: group.groupIDps,
          mergedUploadCount: group.mergedUploadCount,
          playerCount: roster.length,
          totalDamage,
          totalHealing,
          representative,
        };
      });

      const topSkills = topSkillsByParticipant(
        groupsWithRepresentative
          .map((g) => g.representative?.participantId)
          .filter((id): id is number => id != null),
      );
      const topGroupsWithSkills = groupsWithRepresentative.map((g) => ({
        ...g,
        representative: g.representative
          ? { ...g.representative, topSkills: topSkills.get(g.representative.participantId) ?? [] }
          : null,
      }));

      return reply.send({ boss: bossResponse, topGroups: topGroupsWithSkills, topByClass: {} });
    },
  );
}
