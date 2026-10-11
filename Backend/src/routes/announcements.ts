import type { FastifyInstance } from "fastify";
import { announcementList, latestPosted } from "../announcements.js";

export async function announcementRoutes(app: FastifyInstance) {
  // ?lang=<site language>; languages NC has no board in get the English list (the answer names the language used).
  app.get<{ Querystring: { lang?: string } }>("/api/announcements", async (request, reply) => {
    reply.header("Cache-Control", "public, max-age=300");
    return announcementList(request.query.lang);
  });
  // What the client's "New" badge compares: the posting time (epoch seconds) of the newest announcement.
  app.get("/api/announcements/latest", async (_request, reply) => {
    reply.header("Cache-Control", "public, max-age=300");
    return { latest: latestPosted() };
  });
}
