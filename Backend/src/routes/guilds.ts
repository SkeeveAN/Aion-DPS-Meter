import type { FastifyInstance } from "fastify";
import { findGuild, listGuilds } from "../guilds.js";

export async function guildRoutes(app: FastifyInstance) {
  app.get("/api/guilds", async (_request, reply) => {
    const guilds = listGuilds({ stats: true }).sort((a, b) => b.memberCount - a.memberCount || a.name.localeCompare(b.name));
    return reply.header("Cache-Control", "public, max-age=300").send(guilds);
  });

  app.get<{ Params: { slug: string } }>("/api/guilds/:slug", async (request, reply) => {
    const found = findGuild(request.params.slug);
    if (!found) {
      return reply.status(404).send({ error: "guild_not_found" });
    }
    return reply.header("Cache-Control", "public, max-age=300").send(found);
  });
}
