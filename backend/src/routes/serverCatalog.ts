import { and, asc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { serverCatalog } from "../db/schema.js";
import { gameFromQuery } from "./instances.js";

/** The curated name+version list the client's character registration UI picks from - see
 * schema.ts's serverCatalog remarks for why this is separate from the fingerprint-based
 * `servers` table. `?game=` narrows to one game; absent means classic Aion, which is all a client
 * from before the Aion 2 work knows how to handle (it would otherwise offer Aion 2 servers for a
 * Chat.log-based character). */
export async function serverCatalogRoutes(app: FastifyInstance) {
  app.get<{ Querystring: { game?: string } }>("/api/server-catalog", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const rows = db
      .select({
        id: serverCatalog.id,
        name: serverCatalog.name,
        slug: serverCatalog.slug,
        version: serverCatalog.version,
        kind: serverCatalog.kind,
        game: serverCatalog.game,
      })
      .from(serverCatalog)
      .where(and(eq(serverCatalog.active, true), eq(serverCatalog.game, game)))
      .orderBy(asc(serverCatalog.kind), asc(serverCatalog.name))
      .all();
    return reply.send(rows);
  });
}
