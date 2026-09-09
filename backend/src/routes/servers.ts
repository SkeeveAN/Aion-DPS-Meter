import { asc } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { servers } from "../db/schema.js";

/** Lets the frontend offer a server picker before asking for any leaderboard - GET
 * /api/bosses/:id/leaderboard requires a serverId, and this is the only way to learn one. */
export async function serverRoutes(app: FastifyInstance) {
  app.get("/api/servers", async (_request, reply) => {
    const rows = db
      .select({ id: servers.id, fingerprint: servers.fingerprint, displayName: servers.displayName })
      .from(servers)
      .orderBy(asc(servers.id))
      .all();
    return reply.send(rows);
  });
}
