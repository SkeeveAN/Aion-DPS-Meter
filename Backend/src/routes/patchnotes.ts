import type { FastifyInstance } from "fastify";
import { patchnoteList } from "../patchnotes.js";

export async function patchnoteRoutes(app: FastifyInstance) {
  // ?lang=<site language>; languages NC has no board in get the English notes (the answer names the language used).
  app.get<{ Querystring: { lang?: string } }>("/api/patchnotes", async (request, reply) => {
    reply.header("Cache-Control", "public, max-age=300");
    return patchnoteList(request.query.lang);
  });
}
