import { and, desc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, players } from "../db/schema.js";

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

      const groupsWithRosters = topGroups.map((group) => {
        const roster = db
          .select({
            playerName: players.name,
            className: encounterParticipants.className,
            totalDamage: encounterParticipants.totalDamage,
            idps: encounterParticipants.idps,
          })
          .from(encounterParticipants)
          .innerJoin(players, eq(encounterParticipants.playerId, players.id))
          .where(eq(encounterParticipants.encounterId, group.encounterId))
          .orderBy(desc(encounterParticipants.totalDamage))
          .all();
        return { ...group, roster };
      });

      // "Top 10 per class" has no simple SQL form worth fighting for at this
      // scale (at most a few thousand participant rows per boss) - fetch once,
      // group and cap in JS.
      const allParticipants = db
        .select({
          playerName: players.name,
          className: encounterParticipants.className,
          idps: encounterParticipants.idps,
          totalDamage: encounterParticipants.totalDamage,
          encounterId: encounterParticipants.encounterId,
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

      return reply.send({
        boss: { id: boss.id, name: boss.name },
        topGroups: groupsWithRosters,
        topByClass,
      });
    },
  );
}
