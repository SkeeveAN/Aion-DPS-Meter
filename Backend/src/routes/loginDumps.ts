import type { FastifyInstance } from "fastify";
import { z } from "zod";
import { sql } from "drizzle-orm";
import { db } from "../db/client.js";
import { loginDumps } from "../db/schema.js";
import { requireCurrentClient } from "../clientPolicy.js";
import { hashIp } from "../ipHash.js";

/**
 * Login traffic of members of one legion, kept for analysis only (what in a login ties the characters of one player together).
 * The client sends it for Akatsuki on Kaisinel and nobody else; this route checks that again and throws away everything else unread.
 * Rows are purged after LOGIN_DUMP_KEEP_DAYS (default 14).
 */
const LEGION = "akatsuki";
const SERVER_ID = 1304; // Kaisinel (Elyos); Aion 2 reuses the id in other regions, the game server address below tells them apart
const MAX_BYTES = 12 * 1024 * 1024;

const bodySchema = z.object({
  clientVersion: z.string().max(40).default(""),
  characterName: z.string().trim().min(1).max(40),
  guild: z.string().trim().min(1).max(60),
  serverId: z.number().int().positive().max(99999),
  gameServer: z.string().trim().max(64).optional(),
  dump: z.string().min(1), // base64 of gzip(JSON lines)
});

export async function loginDumpRoutes(app: FastifyInstance) {
  app.post("/api/uploads/login-dump", { preHandler: requireCurrentClient, bodyLimit: 20 * 1024 * 1024 }, async (request, reply) => {
    const parsed = bodySchema.safeParse(request.body);
    if (!parsed.success) {
      return reply.status(400).send({ error: "invalid_payload" });
    }
    const p = parsed.data;
    if (p.guild.toLowerCase() !== LEGION || p.serverId !== SERVER_ID) {
      return reply.send({ status: "ignored" }); // not that legion: nothing is stored
    }
    const dump = Buffer.from(p.dump, "base64");
    if (dump.length === 0 || dump.length > MAX_BYTES || dump[0] !== 0x1f || dump[1] !== 0x8b) {
      return reply.status(400).send({ error: "invalid_dump" });
    }
    db.insert(loginDumps)
      .values({ clientVersion: p.clientVersion, characterName: p.characterName, guild: p.guild, clientServerId: p.serverId, gameServer: p.gameServer, ipHash: hashIp(request.ip), bytes: dump.length, dump })
      .run();
    return reply.send({ status: "stored" });
  });
}

/** Deletes login dumps older than the keep time, once at start and then every few hours. */
export function startLoginDumpPurge(): void {
  if (process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const days = Number(process.env.LOGIN_DUMP_KEEP_DAYS ?? 14);
  const purge = () => {
    try {
      db.delete(loginDumps).where(sql`${loginDumps.receivedAt} < datetime('now', ${`-${days} days`})`).run();
    } catch {
      // the table may not exist yet before the first migration
    }
  };
  purge();
  setInterval(purge, 6 * 3600_000).unref();
}
