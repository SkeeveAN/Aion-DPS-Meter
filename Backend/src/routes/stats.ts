import { count, countDistinct, eq, sql } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, instances } from "../db/schema.js";
import { gameFromQuery } from "./instances.js";

/**
 * Honest, site-wide totals for the homepage's data bar (aiondps_design_pack_v1 startseite brief) -
 * scoped by game through the real encounter chain (instances.game), not through `servers` (which
 * carries no game column of its own - see servers.ts's own remarks). A fresh game with zero
 * uploads reports zeros, not a fabricated placeholder; the frontend hides the whole bar rather
 * than show a row of zeroes (see Web-Frontend/app.js renderHome).
 */
export async function statsRoutes(app: FastifyInstance) {
  app.get<{ Querystring: { game?: string } }>("/api/stats/summary", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }

    const encounterRow = db
      .select({ encounterCount: count(encounters.id) })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(eq(instances.game, game))
      .get();

    // One "parse" = one participant's own row in one fight (matches the meter's own vocabulary -
    // see the client's DpsCalculator); distinct playerId across those same rows is the real,
    // ever-uploaded player count for this game, not a fabricated "active" figure with a recency
    // window this data can't back yet.
    const participantRow = db
      .select({
        parseCount: count(encounterParticipants.id),
        playerCount: countDistinct(encounterParticipants.playerId),
      })
      .from(encounterParticipants)
      .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(eq(instances.game, game))
      .get();

    return reply.send({
      encounterCount: encounterRow?.encounterCount ?? 0,
      parseCount: participantRow?.parseCount ?? 0,
      playerCount: participantRow?.playerCount ?? 0,
    });
  });

  // Real per-day encounter counts for a "Performance Overview"-style trend widget - a day with no
  // uploads simply doesn't appear, never a padded/interpolated zero pretending activity happened.
  app.get<{ Querystring: { game?: string; days?: string } }>("/api/stats/daily", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const days = Math.min(Math.max(Number(request.query.days ?? 14) || 14, 1), 90);

    const rows = db
      .select({ day: sql<string>`substr(${encounters.startedAt}, 1, 10)`, encounterCount: count(encounters.id) })
      .from(encounters)
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(eq(instances.game, game))
      .groupBy(sql`substr(${encounters.startedAt}, 1, 10)`)
      .orderBy(sql`substr(${encounters.startedAt}, 1, 10) desc`)
      .limit(days)
      .all();

    return reply.send(rows.reverse());
  });
}
