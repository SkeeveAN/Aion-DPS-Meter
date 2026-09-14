import { ne, eq, and, asc, inArray } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, instances, serverCatalogInstances } from "../db/schema.js";
import { UNASSIGNED_INSTANCE_NAME } from "../constants.js";

export async function instanceRoutes(app: FastifyInstance) {
  // serverCatalogId scopes the list to what that particular server actually offers (per the user,
  // this genuinely differs - Origin Aion/EuroAion share one list, Riftshade's is wider, and a
  // level-65 server has no reason to show a much-lower-level instance from an earlier patch
  // either). Omitted entirely (not just invalid), this returns every instance - kept for now since
  // nothing outside this app's own frontend calls this route without one, but not something a
  // server picker should rely on: a server with no server_catalog_instances rows yet returns
  // EMPTY, never a guessed fallback list (see schema.ts's own remarks - unmapped means uncurated,
  // not "show everything").
  app.get<{ Querystring: { serverCatalogId?: string } }>("/api/instances", async (request, reply) => {
    const rawId = request.query.serverCatalogId;
    let instanceIds: number[] | null = null;
    if (rawId !== undefined) {
      const serverCatalogId = Number(rawId);
      if (!Number.isInteger(serverCatalogId)) {
        return reply.status(400).send({ error: "invalid_server_catalog_id" });
      }
      instanceIds = db
        .select({ instanceId: serverCatalogInstances.instanceId })
        .from(serverCatalogInstances)
        .where(eq(serverCatalogInstances.serverCatalogId, serverCatalogId))
        .all()
        .map((r) => r.instanceId);
    }

    const rows = db
      .select({ id: instances.id, name: instances.name, sortOrder: instances.sortOrder })
      .from(instances)
      .where(
        instanceIds === null
          ? ne(instances.name, UNASSIGNED_INSTANCE_NAME)
          : and(ne(instances.name, UNASSIGNED_INSTANCE_NAME), inArray(instances.id, instanceIds)),
      )
      .orderBy(asc(instances.sortOrder), asc(instances.name))
      .all();
    return reply.send(rows);
  });

  app.get<{ Params: { id: string } }>("/api/instances/:id/bosses", async (request, reply) => {
    const instanceId = Number(request.params.id);
    if (!Number.isInteger(instanceId)) {
      return reply.status(400).send({ error: "invalid_instance_id" });
    }

    const rows = db
      .select({ id: bosses.id, name: bosses.name })
      .from(bosses)
      .where(and(eq(bosses.instanceId, instanceId), eq(bosses.isTrashMob, false)))
      .orderBy(asc(bosses.name))
      .all();
    return reply.send(rows);
  });
}
