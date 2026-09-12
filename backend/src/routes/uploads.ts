import { createHash } from "node:crypto";
import type { FastifyInstance } from "fastify";
import { uploadSchema } from "../uploadSchema.js";
import { processUpload } from "../matching/merge.js";
import { db } from "../db/client.js";
import { uploads } from "../db/schema.js";
import { isTrashMobName } from "../npc/trashMobs.js";

const IP_HASH_SALT = process.env.IP_HASH_SALT ?? "dpsmeter-dev-salt";

function hashIp(ip: string): string {
  return createHash("sha256").update(IP_HASH_SALT).update(ip).digest("hex");
}

export async function uploadRoutes(app: FastifyInstance) {
  app.post("/api/uploads", async (request, reply) => {
    const parseResult = uploadSchema.safeParse(request.body);
    if (!parseResult.success) {
      app.log.warn({ details: parseResult.error.flatten(), body: request.body }, "upload rejected: invalid payload");
      return reply.status(400).send({ error: "invalid_payload", details: parseResult.error.flatten() });
    }
    const payload = parseResult.data;

    // Per the user: an everyday trash mob (e.g. "Kobold Peon") is not a boss fight and must never
    // even be accepted, whether or not the uploading client itself already filters it out (an
    // older client that predates that client-side gate must not be able to smuggle one in) - see
    // npc/trashMobs.ts for how this is decided and why it never rejects an unrecognized name.
    if (isTrashMobName(payload.bossNpcName)) {
      app.log.warn({ bossNpcName: payload.bossNpcName }, "upload rejected: trash mob");
      return reply.status(400).send({ error: "trash_mob_rejected" });
    }

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

    return reply.send(result);
  });
}
