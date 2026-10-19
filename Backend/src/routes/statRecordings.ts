import { gzipSync } from "node:zlib";
import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { db } from "../db/client.js";
import { statRecordings } from "../db/schema.js";
import { requireCurrentClient } from "../clientPolicy.js";

/**
 * A recording of the client's stat calculator, sent by the player after the player has seen what it holds (the stat table of the character, the hits and rates of the
 * recorded fight, the buffs). It is kept for analysis only, to learn how the stats of this game really add up, and linked to nothing else: no player row, no upload, no
 * encounter. It carries no character name, no legion and no IP address (none is stored here either: the rate limit counts in memory). anonymousId is a random value that one
 * installation made for itself.
 */
const MAX_BODY = 2 * 1024 * 1024;

const statMap = z.record(z.string().regex(/^\d{1,5}$/), z.number().int().min(-2_000_000_000).max(2_000_000_000)).refine((m) => Object.keys(m).length <= 700, "too many stats");

const bodySchema = z
  .object({
    kind: z.literal("stat-recording"),
    formatVersion: z.number().int().min(1).max(99),
    clientVersion: z.string().max(40).default(""),
    anonymousId: z.string().regex(/^[0-9a-f]{32}$/),
    mode: z.enum(["simple", "expert"]),
    startedAt: z.string().max(40),
    endedAt: z.string().max(40),
    durationSeconds: z.number().int().min(1).max(3600),
    character: z.object({
      classId: z.number().int().min(1).max(8).nullable(),
      level: z.number().int().min(1).max(200).nullable(),
      serverId: z.number().int().positive().max(99999).nullable(),
      faction: z.enum(["Elyos", "Asmodian", "Unknown"]),
    }),
    statsStart: statMap,
    statsEnd: statMap,
    statChanges: z.array(z.object({ at: z.number().min(0).max(3600), id: z.number().int().min(0).max(99999), value: z.number().int() })).max(5000),
  })
  .passthrough();

export async function statRecordingRoutes(app: FastifyInstance) {
  app.post(
    "/api/uploads/stat-recording",
    { preHandler: requireCurrentClient, bodyLimit: MAX_BODY, config: { rateLimit: { max: 10, timeWindow: "1 hour" } } },
    async (request, reply) => {
      const parsed = bodySchema.safeParse(request.body);
      if (!parsed.success) {
        return reply.status(400).send({ error: "invalid_payload" });
      }
      const p = parsed.data;
      // a name or a legion has no place in it: refuse it unread rather than store it by accident
      const raw = request.body as { character?: object };
      const forbidden = ["name", "characterName", "guild", "legion", "ip"].filter((key) => key in raw || key in (raw.character ?? {}));
      if (forbidden.length > 0) {
        return reply.status(400).send({ error: "personal_data_not_accepted" });
      }
      const payload = gzipSync(Buffer.from(JSON.stringify(request.body)));
      db.insert(statRecordings)
        .values({
          clientVersion: p.clientVersion,
          anonId: p.anonymousId,
          classId: p.character.classId,
          level: p.character.level,
          serverId: p.character.serverId,
          faction: p.character.faction,
          mode: p.mode,
          durationSeconds: p.durationSeconds,
          bytes: payload.length,
          payload,
        })
        .run();
      return reply.send({ status: "stored" });
    },
  );
}
