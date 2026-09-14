import { and, desc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, players } from "../db/schema.js";
import { topBuffsByParticipant, type TopBuff } from "../skills/topBuffs.js";

const TOP_N = 10;

export async function bossRoutes(app: FastifyInstance) {
  // serverId has no cross-server default: per the user, different servers' gear standards are
  // incomparable, so there is no sane "all servers" leaderboard to fall back to - a real, present
  // serverId always scopes to just that one. Absent entirely is its own case, not "all servers"
  // (see below) - a server_catalog entry nobody has uploaded from yet.
  app.get<{ Params: { id: string }; Querystring: { serverId?: string } }>(
    "/api/bosses/:id/leaderboard",
    async (request, reply) => {
      const bossId = Number(request.params.id);
      if (!Number.isInteger(bossId)) {
        return reply.status(400).send({ error: "invalid_boss_id" });
      }

      // Present but invalid (not a real number) is a genuine bad request; ABSENT is the frontend
      // browsing a server_catalog entry with no real `servers` row yet (see servers.ts's own
      // remarks) - there can be no encounters for it either way, so this returns the same empty
      // shape a real serverId with zero uploads would, rather than rejecting the request outright.
      let serverId: number | null = null;
      if (request.query.serverId !== undefined) {
        serverId = Number(request.query.serverId);
        if (!Number.isInteger(serverId)) {
          return reply.status(400).send({ error: "missing_or_invalid_server_id" });
        }
      }

      const boss = db.select().from(bosses).where(eq(bosses.id, bossId)).get();
      if (!boss) {
        return reply.status(404).send({ error: "boss_not_found" });
      }

      const bossResponse = {
        id: boss.id,
        name: boss.name,
        instanceId: boss.instanceId,
        isSolo: boss.isSolo,
        lootRules: boss.lootRules,
      };

      if (serverId === null) {
        return reply.send(
          boss.isSolo
            ? { boss: bossResponse, topGroups: [], topByClass: {} }
            : { boss: bossResponse, topGroups: [] },
        );
      }

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
        const topBuffs = topBuffsByParticipant(topParticipantIds);
        for (const list of Object.values(topByClass)) {
          for (const p of list as any[]) {
            p.topBuffs = topBuffs.get(p.participantId) ?? [];
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

      // roster carries every member's name (per the user: a group row must show everyone, not
      // just one "face of this run") - representative (highest damage dealer, roster[0] once
      // sorted by damage) is kept alongside it only to pick a face/class-icon for the row.
      const groupsWithRoster = topGroups.map((group) => {
        const roster = db
          .select({
            participantId: encounterParticipants.id,
            playerName: players.name,
            className: encounterParticipants.className,
            faction: encounterParticipants.faction,
            totalDamage: encounterParticipants.totalDamage,
            totalHealing: encounterParticipants.totalHealing,
            damageTaken: encounterParticipants.damageTaken,
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
          roster,
          representative,
        };
      });

      // Per the user: a group row's Buffs must reflect the whole group's reinforcements, not just
      // its top damage dealer's - summed by skill across every member, then capped the same way a
      // single participant's own topBuffs already is.
      const buffsByParticipant = topBuffsByParticipant(groupsWithRoster.flatMap((g) => g.roster.map((p) => p.participantId)));
      const topGroupsWithBuffs = groupsWithRoster.map((g) => {
        const castsBySkill = new Map<string, TopBuff>();
        for (const p of g.roster) {
          for (const buff of buffsByParticipant.get(p.participantId) ?? []) {
            const existing = castsBySkill.get(buff.skillName);
            castsBySkill.set(buff.skillName, { ...buff, casts: (existing?.casts ?? 0) + buff.casts });
          }
        }

        const groupBuffs = [...castsBySkill.values()].sort((a, b) => b.casts - a.casts).slice(0, 8);
        return { ...g, groupBuffs };
      });

      return reply.send({ boss: bossResponse, topGroups: topGroupsWithBuffs, topByClass: {} });
    },
  );
}
