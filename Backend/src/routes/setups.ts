import type { FastifyInstance, FastifyReply, FastifyRequest } from "fastify";
import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { players } from "../db/schema.js";
import { hashIp, hashVoter } from "../ipHash.js";
import { buildProfileView } from "../profile.js";
import { parseIdOrSlug } from "../seo/slug.js";
import { addThumb, moveThumb, removeThumb, setupById, setupsOfPlayer, MAX_THUMBS_PER_ADDRESS } from "../setups.js";

/**
 * Thumbs on the boards and skill builds of a character (setups.ts).
 *
 * The reader is recognised by a random id that the page keeps in the browser's local storage and sends in the `X-Voter` header (never in the address, so that it
 * does not end up in a log); the server stores only a keyed hash of it, and a keyed hash of the network address (see the schema of setup_votes for why both).
 * The page's own cookies stay what they were: none.
 */
const VOTER = /^[0-9a-f]{32,64}$/;

/** The reader's hashed id from the header, null when there is none (a reader that only looks) and `undefined` when the header is there but not an id. */
function voterOf(request: FastifyRequest): string | null | undefined {
  const raw = request.headers["x-voter"];
  if (raw === undefined) {
    return null;
  }
  return typeof raw === "string" && VOTER.test(raw) ? hashVoter(raw) : undefined;
}

function voterOrReject(request: FastifyRequest, reply: FastifyReply): string | null {
  const voter = voterOf(request);
  if (!voter) {
    void reply.status(400).send({ error: "voter_required" });
    return null;
  }
  return voter;
}

function setupId(raw: string): number | null {
  return /^\d{1,9}$/.test(raw) ? Number(raw) : null;
}

const STATUS = { not_found: 404, superseded: 409, address_limit: 429, nothing_to_transfer: 409 } as const;

// Writing is limited per address on top of the limit per state; reading is not limited (cheap, indexed).
const WRITE_LIMIT = { rateLimit: { max: 40, timeWindow: "1 hour" } };

export async function setupRoutes(app: FastifyInstance) {
  /** The thumbs of a character: for boards and skills the current state and the earlier states that kept thumbs. */
  app.get<{ Params: { id: string } }>("/api/players/:id/setups", async (request, reply) => {
    const key = parseIdOrSlug(request.params.id);
    if (!key) {
      return reply.status(400).send({ error: "invalid_player_id" });
    }
    const voter = voterOf(request);
    if (voter === undefined) {
      return reply.status(400).send({ error: "invalid_voter" });
    }
    const player = db
      .select({ id: players.id, deletedAt: players.deletedAt })
      .from(players)
      .where("id" in key ? eq(players.id, key.id) : eq(players.slug, key.slug))
      .get();
    if (!player || player.deletedAt !== null) {
      return reply.status(404).send({ error: "player_not_found" });
    }
    reply.header("Cache-Control", "no-store");
    return { ...setupsOfPlayer(player.id, voter), maxPerAddress: MAX_THUMBS_PER_ADDRESS };
  });

  /** An earlier state with its content, drawn by the page like the character's own boards or skills. */
  app.get<{ Params: { id: string } }>("/api/setups/:id", async (request, reply) => {
    const id = setupId(request.params.id);
    const row = id === null ? null : setupById(id);
    if (!row) {
      return reply.status(404).send({ error: "setup_not_found" });
    }
    const view = row.kind === "board" ? buildProfileView(row.playerId, { daevanionJson: row.dataJson }) : buildProfileView(row.playerId, { skillsJson: row.dataJson });
    if (!view) {
      return reply.status(404).send({ error: "setup_not_found" });
    }
    reply.header("Cache-Control", "no-store");
    return {
      id: row.id,
      kind: row.kind,
      createdAt: row.createdAt,
      supersededAt: row.supersededAt,
      thumbs: row.thumbs,
      className: view.className,
      skills: view.skills,
      daevanion: view.daevanion,
    };
  });

  app.post<{ Params: { id: string } }>("/api/setups/:id/thumb", { config: WRITE_LIMIT }, async (request, reply) => {
    const id = setupId(request.params.id);
    const voter = voterOrReject(request, reply);
    if (id === null || !voter) {
      return id === null ? reply.status(404).send({ error: "setup_not_found" }) : reply;
    }
    const result = addThumb(id, voter, hashIp(request.ip));
    return result.ok ? { thumbs: result.thumbs, voted: result.voted } : reply.status(STATUS[result.error]).send({ error: result.error });
  });

  app.delete<{ Params: { id: string } }>("/api/setups/:id/thumb", { config: WRITE_LIMIT }, async (request, reply) => {
    const id = setupId(request.params.id);
    const voter = voterOrReject(request, reply);
    if (id === null || !voter) {
      return id === null ? reply.status(404).send({ error: "setup_not_found" }) : reply;
    }
    const result = removeThumb(id, voter);
    return result.ok ? { thumbs: result.thumbs, voted: result.voted } : reply.status(STATUS[result.error]).send({ error: result.error });
  });

  /** The reader's thumb on an earlier state moves to the character's current state. */
  app.post<{ Params: { id: string } }>("/api/setups/:id/transfer", { config: WRITE_LIMIT }, async (request, reply) => {
    const id = setupId(request.params.id);
    const voter = voterOrReject(request, reply);
    if (id === null || !voter) {
      return id === null ? reply.status(404).send({ error: "setup_not_found" }) : reply;
    }
    const result = moveThumb(id, voter, hashIp(request.ip));
    return result.ok ? { toId: result.toId, thumbs: result.thumbs, voted: true, fromThumbs: result.fromThumbs } : reply.status(STATUS[result.error]).send({ error: result.error });
  });
}
