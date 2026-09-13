import { asc, eq } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { serverCatalog, servers } from "../db/schema.js";

/**
 * Lets the frontend offer a server picker before asking for any leaderboard - GET
 * /api/bosses/:id/leaderboard requires a serverId, and this is the only way to learn one.
 *
 * Per the user: this must list every server the CLIENT's own server-registration picker offers
 * (see server-catalog.ts/serverCatalog's own remarks), not just the ones that happen to have
 * already uploaded a fight - a server like EuroAion is real and selectable in the client today
 * even though nobody has uploaded from it yet. Left-joined against `servers` (matched by name,
 * the same string both tables use - "Origin Aion" in both) so a catalog entry that DOES already
 * have real uploads still resolves to its real serverId (needed for the leaderboard query above);
 * one that doesn't yet gets `id: null` instead of a fabricated serverId, which would either violate
 * `servers.fingerprint`'s NOT NULL/UNIQUE constraint or create a stray row a later REAL upload from
 * that same server (with its own real fingerprint) could never match back up with (see
 * matching/merge.ts's own fingerprint-based upsert). The frontend disables click-through for a null
 * id rather than querying a leaderboard with no serverId at all.
 */
export async function serverRoutes(app: FastifyInstance) {
  app.get("/api/servers", async (_request, reply) => {
    const rows = db
      .select({
        id: servers.id,
        fingerprint: servers.fingerprint,
        name: serverCatalog.name,
        kind: serverCatalog.kind,
      })
      .from(serverCatalog)
      .leftJoin(servers, eq(servers.displayName, serverCatalog.name))
      .where(eq(serverCatalog.active, true))
      .orderBy(asc(serverCatalog.kind), asc(serverCatalog.name))
      .all();
    return reply.send(rows);
  });
}
