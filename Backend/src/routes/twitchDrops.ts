import type { FastifyInstance } from "fastify";
import { twitchDropsStatus, twitchDropsView } from "../twitchDrops.js";

export async function twitchDropsRoutes(app: FastifyInstance) {
  // A few numbers for the meter's Twitch icon (no identifier of the player is involved): running, start of the newest campaign, end of the running ones.
  app.get("/api/twitch-drops/latest", async (_request, reply) => {
    reply.header("Cache-Control", "public, max-age=300");
    return twitchDropsStatus();
  });
  app.get("/api/twitch-drops", async (_request, reply) => {
    reply.header("Cache-Control", "public, max-age=120");
    return twitchDropsView();
  });
}
