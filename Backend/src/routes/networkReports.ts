import { gzipSync } from "node:zlib";
import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { db } from "../db/client.js";
import { networkReports } from "../db/schema.js";
import { requireCurrentClient } from "../clientPolicy.js";

/**
 * A network report of the last fifteen minutes, sent by the player after the player has seen what it holds (see the client's Aion2/Capture/NetworkReport.cs): the quality
 * of the game connection, the probe of the router and the internet, the pauses, the kind of adapter and the game server's address. It carries no IP address of the player, no
 * name of a PC, an account, an adapter or a character; none is stored here either (the rate limit counts in memory). Linked to nothing else.
 */
const MAX_BODY = 1024 * 1024;

const bodySchema = z
  .object({
    format: z.literal("aiondps-network-report"),
    formatVersion: z.number().int().min(1).max(99),
    clientVersion: z.string().max(40).default(""),
    anonymousId: z.string().regex(/^[0-9a-f]{32}$/),
    periodSeconds: z.number().int().min(0).max(3600),
    stalls: z.array(z.object({}).passthrough()).max(200),
    timeline: z.object({ bucketSeconds: z.number().int().min(1).max(600), rows: z.array(z.object({}).passthrough()).max(1000) }),
  })
  .passthrough();

export async function networkReportRoutes(app: FastifyInstance) {
  app.post(
    "/api/diagnostics/network",
    { preHandler: requireCurrentClient, bodyLimit: MAX_BODY, config: { rateLimit: { max: 20, timeWindow: "1 hour" } } },
    async (request, reply) => {
      const parsed = bodySchema.safeParse(request.body);
      if (!parsed.success) {
        return reply.status(400).send({ error: "invalid_payload" });
      }
      const p = parsed.data;
      const payload = gzipSync(Buffer.from(JSON.stringify(request.body)));
      db.insert(networkReports)
        .values({ clientVersion: p.clientVersion, anonId: p.anonymousId, periodSeconds: p.periodSeconds, stallCount: p.stalls.length, bytes: payload.length, payload })
        .run();
      return reply.send({ status: "stored" });
    },
  );
}
