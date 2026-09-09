import { desc, eq, like } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, players } from "../db/schema.js";
import { normalizeName } from "../matching/roster.js";

export async function playerRoutes(app: FastifyInstance) {
  app.get<{ Querystring: { q?: string } }>("/api/players/search", async (request, reply) => {
    const query = (request.query.q ?? "").trim();
    if (query.length === 0) {
      return reply.send([]);
    }

    const rows = db
      .select({ id: players.id, name: players.name })
      .from(players)
      .where(like(players.nameNormalized, `%${normalizeName(query)}%`))
      .limit(20)
      .all();
    return reply.send(rows);
  });

  app.get<{ Params: { id: string } }>("/api/players/:id", async (request, reply) => {
    const playerId = Number(request.params.id);
    if (!Number.isInteger(playerId)) {
      return reply.status(400).send({ error: "invalid_player_id" });
    }

    const player = db.select().from(players).where(eq(players.id, playerId)).get();
    if (!player) {
      return reply.status(404).send({ error: "player_not_found" });
    }

    const history = db
      .select({
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

    return reply.send({ player: { id: player.id, name: player.name }, history });
  });
}
