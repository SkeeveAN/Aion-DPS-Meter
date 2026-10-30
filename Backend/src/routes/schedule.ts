import { readFileSync, statSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { FastifyInstance } from "fastify";

// The timetable of the in-game events (battlefield, Abyss raid, rifts ...), the one the client's overlay shows. The file is the same one the client ships as its
// fallback (Client/assets/aion2/schedule/events.json, the same way partnerApi.ts reads the client's assets); the client asks for it at the start and keeps the last
// answer, so a changed time only has to be changed here and the client needs no new release.
export const SCHEDULE_FILE = process.env.SCHEDULE_FILE ?? fileURLToPath(new URL("../../../Client/assets/aion2/schedule/events.json", import.meta.url));

let cached: { mtimeMs: number; body: string } | null = null;

/** The file's text, read again only when it changed. Throws when it is missing or not a timetable. */
export function scheduleBody(file = SCHEDULE_FILE): string {
  const mtimeMs = statSync(file).mtimeMs;
  if (cached === null || cached.mtimeMs !== mtimeMs || file !== SCHEDULE_FILE) {
    const body = readFileSync(file, "utf8");
    const parsed = JSON.parse(body) as { events?: unknown };
    if (!Array.isArray(parsed.events)) {
      throw new Error("schedule file has no events list");
    }
    if (file !== SCHEDULE_FILE) {
      return body;
    }
    cached = { mtimeMs, body };
  }
  return cached.body;
}

export async function scheduleRoutes(app: FastifyInstance) {
  app.get("/api/schedule", async (_request, reply) => {
    try {
      const body = scheduleBody();
      reply.header("Cache-Control", "public, max-age=300");
      return reply.type("application/json; charset=utf-8").send(body);
    } catch (err) {
      app.log.error({ err }, "schedule file unreadable");
      return reply.code(503).send({ error: "schedule unavailable" });
    }
  });
}
