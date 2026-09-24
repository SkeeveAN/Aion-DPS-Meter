import { and, desc, eq, like, max } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, instances, players, servers } from "../db/schema.js";
import { normalizeName } from "../matching/roster.js";
import { gameFromQuery } from "./instances.js";
import type { Game } from "../constants.js";

/**
 * Each player's own single best fight (highest iDPS), ranked - the homepage's "top players"
 * widget. Per the user, classic-Aion servers are never comparable (different gear standards), so
 * this only ever runs scoped to one explicit server there; Aion 2's official servers share one
 * standard and are combined by default (serverId null) - same rule bosses.ts's leaderboard
 * already follows, just applied to a query that spans every boss instead of one.
 *
 * No window function: GROUP BY + MAX already gives the top N (playerId, bestIdps) pairs in one
 * query, and a second per-player lookup (N is always small, 5-10) finds which real fight achieved
 * it - the same "small N, one extra query per row" shape topGroups already uses for its roster.
 */
export function topPlayersOverall(game: Game, serverId: number | null, limit: number) {
  const scope = serverId === null ? eq(instances.game, game) : and(eq(instances.game, game), eq(encounters.serverId, serverId));

  const bestPerPlayer = db
    .select({ playerId: encounterParticipants.playerId, bestIdps: max(encounterParticipants.idps) })
    .from(encounterParticipants)
    .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .where(scope)
    .groupBy(encounterParticipants.playerId)
    .orderBy(desc(max(encounterParticipants.idps)))
    .limit(limit)
    .all();

  return bestPerPlayer
    .filter((row): row is { playerId: number; bestIdps: number } => row.bestIdps !== null)
    .map(({ playerId, bestIdps }) => {
      // Ties (or a rounding-equal idps from two different fights) resolve to the most recent one.
      const row = db
        .select({
          encounterId: encounters.id,
          playerName: players.name,
          serverName: servers.displayName,
          className: encounterParticipants.className,
          idps: encounterParticipants.idps,
          bossName: bosses.name,
          bossNameEn: bosses.nameEn,
          bossSlug: bosses.slug,
          instanceName: instances.name,
          instanceNameEn: instances.nameEn,
          instanceSlug: instances.slug,
          startedAt: encounters.startedAt,
        })
        .from(encounterParticipants)
        .innerJoin(players, eq(encounterParticipants.playerId, players.id))
        .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
        .innerJoin(bosses, eq(encounters.bossId, bosses.id))
        .innerJoin(instances, eq(bosses.instanceId, instances.id))
        .leftJoin(servers, eq(encounters.serverId, servers.id))
        .where(and(eq(encounterParticipants.playerId, playerId), eq(encounterParticipants.idps, bestIdps)))
        .orderBy(desc(encounters.startedAt))
        .get();
      return row;
    })
    .filter((row) => row !== undefined);
}

export async function playerRoutes(app: FastifyInstance) {
  app.get<{ Querystring: { game?: string; serverId?: string; limit?: string } }>("/api/players/top", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const limit = Math.min(Math.max(Number(request.query.limit ?? 5) || 5, 1), 20);

    let serverId: number | null = null;
    if (request.query.serverId !== undefined) {
      serverId = Number(request.query.serverId);
      if (!Number.isInteger(serverId)) {
        return reply.status(400).send({ error: "invalid_server_id" });
      }
    } else if (game === "aion") {
      // No implicit "busiest server" default here (unlike a boss leaderboard) - this ranking spans
      // every boss, and classic-Aion servers are never comparable, so there is no honest combined
      // answer to fall back to. Empty beats wrong.
      return reply.send([]);
    }

    return reply.send(topPlayersOverall(game, serverId, limit));
  });

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
        faction: encounterParticipants.faction,
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
