import type { FastifyInstance } from "fastify";
import { changelogFeed, isVersion } from "../changelogFeed.js";

export async function changelogRoutes(app: FastifyInstance) {
  // What the client's update window shows: the client changes after ?from= (the installed version) up to ?to= (the offered one), one line each, in ?lang=
  // (German or English). Plain GET without any identifier; part of the permission to update, so it is asked in restricted mode too.
  app.get<{ Querystring: { from?: string; to?: string; lang?: string } }>("/api/changelog", async (request, reply) => {
    const { from, to, lang } = request.query;
    if (!isVersion(from) || (to !== undefined && !isVersion(to))) {
      return reply.code(400).send({ error: "from and to must be versions like 1.18.2" });
    }

    reply.header("Cache-Control", "public, max-age=300");
    return { versions: changelogFeed(from, to, lang === "de" ? "de" : "en") };
  });
}
