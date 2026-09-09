import { asc } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { serverCatalog } from "../db/schema.js";

/** The curated name+version list the client's character registration UI picks from - see
 * schema.ts's serverCatalog remarks for why this is separate from the fingerprint-based
 * `servers` table. */
export async function serverCatalogRoutes(app: FastifyInstance) {
  app.get("/api/server-catalog", async (_request, reply) => {
    const rows = db
      .select({ id: serverCatalog.id, name: serverCatalog.name, version: serverCatalog.version, kind: serverCatalog.kind })
      .from(serverCatalog)
      .orderBy(asc(serverCatalog.kind), asc(serverCatalog.name))
      .all();
    return reply.send(rows);
  });
}
