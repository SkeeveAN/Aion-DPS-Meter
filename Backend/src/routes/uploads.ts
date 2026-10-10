import type { FastifyInstance } from "fastify";
import { profilesUploadSchema, uploadSchema } from "../uploadSchema.js";
import { processProfilesUpload, processUpload } from "../matching/merge.js";
import { db } from "../db/client.js";
import { uploads } from "../db/schema.js";
import { clearPageCache } from "../seo/cache.js";

import { hashIp } from "../ipHash.js";
import { MIN_KILL_DAMAGE_SHARE } from "../constants.js";

export async function uploadRoutes(app: FastifyInstance) {
  // Aion 2 players without a boss fight (a client that read characters but killed nothing). The
  // path stays under /api/uploads on purpose: the rate limiter in server.ts keys on that prefix.
  app.post("/api/uploads/profiles", async (request, reply) => {
    const parseResult = profilesUploadSchema.safeParse(request.body);
    if (!parseResult.success) {
      app.log.warn({ details: parseResult.error.flatten(), issues: parseResult.error.issues.slice(0, 6).map((i) => `${i.path.join(".")}: ${i.message}`), clientVersion: (request.body as { clientVersion?: string } | null)?.clientVersion }, "profiles upload rejected: invalid payload");
      return reply.status(400).send({ error: "invalid_payload", details: parseResult.error.flatten() });
    }
    const payload = parseResult.data;
    if (payload.participants.filter((p) => p.isSelf).length !== 1) {
      return reply.status(400).send({ error: "exactly_one_self_participant_required" });
    }

    let result;
    try {
      result = processProfilesUpload(payload);
    } catch (err) {
      app.log.error(err);
      return reply.status(500).send({ error: "processing_failed" });
    }

    db.insert(uploads)
      .values({
        clientVersion: payload.clientVersion,
        serverId: result.serverId,
        clientServerId: payload.serverId,
        gameServer: payload.gameServer,
        uploaderReportedName: payload.participants.find((p) => p.isSelf)!.name,
        ipHash: hashIp(request.ip),
        status: "merged",
        rawPayloadJson: JSON.stringify(payload),
      })
      .run();
    clearPageCache();
    return reply.send({ status: "profiles", ...result });
  });

  app.post("/api/uploads", async (request, reply) => {
    const parseResult = uploadSchema.safeParse(request.body);
    if (!parseResult.success) {
      app.log.warn({ details: parseResult.error.flatten(), issues: parseResult.error.issues.slice(0, 6).map((i) => `${i.path.join(".")}: ${i.message}`), body: request.body }, "upload rejected: invalid payload");
      return reply.status(400).send({ error: "invalid_payload", details: parseResult.error.flatten() });
    }
    const payload = parseResult.data;

    const selfCount = payload.participants.filter((p) => p.isSelf).length;
    if (selfCount !== 1) {
      app.log.warn({ selfCount, bossNpcName: payload.bossNpcName }, "upload rejected: not exactly one self participant");
      return reply.status(400).send({ error: "exactly_one_self_participant_required" });
    }

    // A fight counts only when the group took the boss down: the damage dealt has to add up to (nearly)
    // all of the boss's hit points. Otherwise two failed 50 % attempts - or an empty run - would pass
    // as one kill. The client sends the boss's highest hit-point reading; without it nothing proves
    // the fight was a whole one.
    const groupDamage = payload.participants.reduce((sum, p) => sum + p.totalDamage, 0);
    if (payload.bossMaxHp === undefined || groupDamage < payload.bossMaxHp * MIN_KILL_DAMAGE_SHARE) {
      app.log.warn({ groupDamage, bossMaxHp: payload.bossMaxHp, bossNpcName: payload.bossNpcName, clientVersion: payload.clientVersion }, "upload rejected: damage does not cover the boss's hit points");
      return reply.status(400).send({ error: "boss_not_killed", groupDamage, bossMaxHp: payload.bossMaxHp ?? null });
    }

    const uploaderReportedName = payload.participants.find((p) => p.isSelf)!.name;
    const ipHash = hashIp(request.ip);

    let result;
    try {
      result = processUpload(payload);
    } catch (err) {
      app.log.error(err);
      db.insert(uploads)
        .values({
          clientVersion: payload.clientVersion,
          uploaderReportedName,
          ipHash,
          status: "rejected",
          rawPayloadJson: JSON.stringify(payload),
        })
        .run();
      return reply.status(500).send({ error: "processing_failed" });
    }

    db.insert(uploads)
      .values({
        clientVersion: payload.clientVersion,
        serverId: result.serverId,
        clientServerId: payload.serverId,
        gameServer: payload.gameServer,
        uploaderReportedName,
        ipHash,
        matchedEncounterId: result.encounterId,
        status: "merged",
        rawPayloadJson: JSON.stringify(payload),
      })
      .run();

    clearPageCache();
    return reply.send(result);
  });
}
