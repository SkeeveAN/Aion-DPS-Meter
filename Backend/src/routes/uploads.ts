import { createHash } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { profilesUploadSchema, uploadSchema } from "../uploadSchema.js";
import { processProfilesUpload, processUpload } from "../matching/merge.js";
import { db } from "../db/client.js";
import { uploads } from "../db/schema.js";
import { clearPageCache } from "../seo/cache.js";

const IP_HASH_SALT = process.env.IP_HASH_SALT ?? "dpsmeter-dev-salt";

function hashIp(ip: string): string {
  return createHash("sha256").update(IP_HASH_SALT).update(ip).digest("hex");
}

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
