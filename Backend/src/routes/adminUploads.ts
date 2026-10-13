import { timingSafeEqual } from "node:crypto";
import type { FastifyInstance, FastifyReply, FastifyRequest } from "fastify";
import { env } from "../env.js";
import { approveUpload, pendingUploads, rejectUpload } from "../uploadReview.js";

function authorised(request: FastifyRequest): boolean {
  if (env.ADMIN_TOKEN.length === 0) {
    return false;
  }

  const given = Buffer.from((request.headers.authorization ?? "").replace(/^Bearer /i, ""));
  const wanted = Buffer.from(env.ADMIN_TOKEN);
  return given.length === wanted.length && timingSafeEqual(given, wanted);
}

/** The review queue for uploads that look implausible (see uploadReview.ts). Switched off (404) while ADMIN_TOKEN is empty; the token goes in "Authorization: Bearer ...". */
export async function adminUploadRoutes(app: FastifyInstance) {
  const guard = async (request: FastifyRequest, reply: FastifyReply) => {
    if (!authorised(request)) {
      return reply.status(404).send({ error: "not_found" });
    }
  };

  app.get("/api/admin/uploads/pending", { preHandler: guard }, async () => ({ pending: pendingUploads() }));

  app.post<{ Params: { id: string } }>("/api/admin/uploads/:id/approve", { preHandler: guard }, async (request, reply) => {
    const id = Number(request.params.id);
    if (!Number.isInteger(id)) {
      return reply.status(400).send({ error: "bad_id" });
    }

    try {
      const result = approveUpload(id);
      return result ? reply.send({ status: "approved", ...result }) : reply.status(404).send({ error: "not_pending" });
    } catch (err) {
      app.log.error(err);
      return reply.status(500).send({ error: "approve_failed" });
    }
  });

  app.post<{ Params: { id: string } }>("/api/admin/uploads/:id/reject", { preHandler: guard }, async (request, reply) => {
    const id = Number(request.params.id);
    if (!Number.isInteger(id)) {
      return reply.status(400).send({ error: "bad_id" });
    }

    return rejectUpload(id) ? reply.send({ status: "rejected" }) : reply.status(404).send({ error: "not_pending" });
  });
}
