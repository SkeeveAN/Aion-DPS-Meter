import type { FastifyInstance } from "fastify";
import { statusSnapshot } from "../status/serverStatus.js";

export async function serverStatusRoutes(app: FastifyInstance) {
  app.get("/api/server-status", async (_request, reply) => {
    reply.header("Cache-Control", "public, max-age=30");
    return statusSnapshot();
  });
}
