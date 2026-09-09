import { ne, eq, asc } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, instances } from "../db/schema.js";
import { UNASSIGNED_INSTANCE_NAME } from "../constants.js";

export async function instanceRoutes(app: FastifyInstance) {
  app.get("/api/instances", async (_request, reply) => {
    const rows = db
      .select({ id: instances.id, name: instances.name, sortOrder: instances.sortOrder })
      .from(instances)
      .where(ne(instances.name, UNASSIGNED_INSTANCE_NAME))
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
      .where(eq(bosses.instanceId, instanceId))
      .orderBy(asc(bosses.name))
      .all();
    return reply.send(rows);
  });
}
