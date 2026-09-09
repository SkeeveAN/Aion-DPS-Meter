import { and, desc, eq, like } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, players, servers } from "../db/schema.js";
import { normalizeName } from "../matching/roster.js";

export async function playerRoutes(app: FastifyInstance) {
  // serverId narrows the search when given; without it, results span every server, which is why
  // each row now carries its own serverId/serverName - a name search across servers legitimately
  // can turn up two unrelated people who happen to share a name, and the caller needs to be able
  // to tell them apart rather than silently picking one.
  app.get<{ Querystring: { q?: string; serverId?: string } }>("/api/players/search", async (request, reply) => {
    const query = (request.query.q ?? "").trim();
    if (query.length === 0) {
      return reply.send([]);
    }

    const serverId = Number(request.query.serverId);
    const nameFilter = like(players.nameNormalized, `%${normalizeName(query)}%`);

    const rows = db
      .select({
        id: players.id,
        name: players.name,
        serverId: players.serverId,
        serverName: servers.displayName,
        serverFingerprint: servers.fingerprint,
      })
      .from(players)
      .leftJoin(servers, eq(players.serverId, servers.id))
      .where(Number.isInteger(serverId) ? and(nameFilter, eq(players.serverId, serverId)) : nameFilter)
      .limit(20)
      .all();
    return reply.send(rows);
  });

  app.get<{ Params: { id: string } }>("/api/players/:id", async (request, reply) => {
    const playerId = Number(request.params.id);
    if (!Number.isInteger(playerId)) {
      return reply.status(400).send({ error: "invalid_player_id" });
    }

    const player = db
      .select({
        id: players.id,
        name: players.name,
        serverId: players.serverId,
        serverName: servers.displayName,
        serverFingerprint: servers.fingerprint,
      })
      .from(players)
      .leftJoin(servers, eq(players.serverId, servers.id))
      .where(eq(players.id, playerId))
      .get();
    if (!player) {
      return reply.status(404).send({ error: "player_not_found" });
    }

    const history = db
      .select({
        participantId: encounterParticipants.id,
        encounterId: encounters.id,
        bossId: bosses.id,
        bossName: bosses.name,
        startedAt: encounters.startedAt,
        className: encounterParticipants.className,
        totalDamage: encounterParticipants.totalDamage,
        idps: encounterParticipants.idps,
        critRatePercent: encounterParticipants.critRatePercent,
      })
      .from(encounterParticipants)
      .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .where(eq(encounterParticipants.playerId, playerId))
      .orderBy(desc(encounters.startedAt))
      .all();

    return reply.send({ player, history });
  });
}
