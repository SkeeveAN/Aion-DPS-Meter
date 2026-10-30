import { readFileSync, statSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { FastifyInstance } from "fastify";

// The timetable of the in-game events (battlefield, Abyss raid, rifts ...), the one the client's overlay shows. The file is the same one the client ships as its
// fallback (Client/assets/aion2/schedule/events.json, the same way partnerApi.ts reads the client's assets); the client asks for it at the start and keeps the last
// answer, so a changed time only has to be changed here and the client needs no new release.
// The times are those of the European clock (the file's "timeZone"). An event can name its own clock per region ("zones": {"nae": "America/New_York"}); the answer
// for ?region=<code> gives each event its own zone for that region and leaves the others on the file's zone. Regions: eu, nae, naw, latam, asia.
export const SCHEDULE_FILE = process.env.SCHEDULE_FILE ?? fileURLToPath(new URL("../../../Client/assets/aion2/schedule/events.json", import.meta.url));
export const SCHEDULE_REGIONS = ["eu", "nae", "naw", "latam", "asia"] as const;

type Table = { timeZone?: string; events: { zones?: Record<string, string>; timeZone?: string }[] };

let cached: { file: string; mtimeMs: number; table: Table; bodies: Map<string, string> } | null = null;

function readTable(file: string): { mtimeMs: number; table: Table } {
  const mtimeMs = statSync(file).mtimeMs;
  const table = JSON.parse(readFileSync(file, "utf8")) as Table;
  if (!Array.isArray(table.events)) {
    throw new Error("schedule file has no events list");
  }
  return { mtimeMs, table };
}

/** The timetable as text for a region (null/unknown region = the file as it is, the European one); read again only when the file changed. Throws when it is missing or not a timetable. */
export function scheduleBody(region: string | null = null, file = SCHEDULE_FILE): string {
  const code = SCHEDULE_REGIONS.find((r) => r === region) ?? null;
  const mtimeMs = statSync(file).mtimeMs;
  if (cached === null || cached.file !== file || cached.mtimeMs !== mtimeMs) {
    const read = readTable(file);
    cached = { file, mtimeMs: read.mtimeMs, table: read.table, bodies: new Map() };
  }
  const key = code ?? "";
  let body = cached.bodies.get(key);
  if (body === undefined) {
    body = JSON.stringify({
      ...cached.table,
      ...(code ? { region: code } : {}),
      events: cached.table.events.map(({ zones, ...event }) => (code && zones?.[code] ? { ...event, timeZone: zones[code] } : event)),
    });
    cached.bodies.set(key, body);
  }
  return body;
}

export async function scheduleRoutes(app: FastifyInstance) {
  app.get<{ Querystring: { region?: string } }>("/api/schedule", async (request, reply) => {
    try {
      const body = scheduleBody(request.query.region ?? null);
      reply.header("Cache-Control", "public, max-age=300");
      return reply.type("application/json; charset=utf-8").send(body);
    } catch (err) {
      app.log.error({ err }, "schedule file unreadable");
      return reply.code(503).send({ error: "schedule unavailable" });
    }
  });
}
