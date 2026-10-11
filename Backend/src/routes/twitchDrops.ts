import type { FastifyInstance } from "fastify";
import { twitchDropsView } from "../twitchDrops.js";

export async function twitchDropsRoutes(app: FastifyInstance) {
  app.get("/api/twitch-drops", async (_request, reply) => {
    reply.header("Cache-Control", "public, max-age=120");
    return twitchDropsView();
  });
}
