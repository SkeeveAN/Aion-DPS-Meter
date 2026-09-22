import { ne, eq, and, asc, inArray } from "drizzle-orm";
import type { FastifyInstance, FastifyReply } from "fastify";
import { db } from "../db/client.js";
import { bosses, instances, serverCatalogInstances } from "../db/schema.js";
import { DEFAULT_GAME, isGame, UNASSIGNED_INSTANCE_NAME, type Game } from "../constants.js";
import { parseIdOrSlug } from "../seo/slug.js";

/** `?game=` is optional everywhere - absent means classic Aion, the only game older callers know. */
export function gameFromQuery(raw: string | undefined, reply: FastifyReply): Game | null {
  if (raw === undefined) {
    return DEFAULT_GAME;
  }
  if (!isGame(raw)) {
    reply.status(400).send({ error: "invalid_game" });
    return null;
  }
  return raw;
}

export const instanceColumns = {
  id: instances.id,
  name: instances.name,
  nameEn: instances.nameEn,
  slug: instances.slug,
  game: instances.game,
  category: instances.category,
  source: instances.source,
  sortOrder: instances.sortOrder,
};

/** Instance by numeric id, or by slug within a game (slugs are only unique per game). */
export function findInstance(idOrSlug: string, game: Game) {
  const key = parseIdOrSlug(idOrSlug);
  if (key === null) {
    return null;
  }
  return db
    .select(instanceColumns)
    .from(instances)
    .where("id" in key ? eq(instances.id, key.id) : and(eq(instances.slug, key.slug), eq(instances.game, game)))
    .get() ?? null;
}

export async function instanceRoutes(app: FastifyInstance) {
  // serverCatalogId scopes the list to what that particular server actually offers (per the user,
  // this genuinely differs - Origin Aion/EuroAion share one list, Riftshade's is wider, and a
  // level-65 server has no reason to show a much-lower-level instance from an earlier patch
  // either). Omitted, this returns every instance of the game. For classic Aion nothing but this
  // app's own frontend does that; for Aion 2 it is the normal case for now - no server-specific
  // curation exists yet (EU/NA servers aren't live), so the derived dungeon list is shown as-is.
  // A server WITH server_catalog_instances rows but none matching returns EMPTY, never a guessed
  // fallback list (see schema.ts's own remarks - unmapped means uncurated, not "show everything").
  app.get<{ Querystring: { serverCatalogId?: string; game?: string } }>("/api/instances", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }

    const rawId = request.query.serverCatalogId;
    let instanceIds: number[] | null = null;
    if (rawId !== undefined) {
      const serverCatalogId = Number(rawId);
      if (!Number.isInteger(serverCatalogId)) {
        return reply.status(400).send({ error: "invalid_server_catalog_id" });
      }
      instanceIds = db
        .select({ instanceId: serverCatalogInstances.instanceId })
        .from(serverCatalogInstances)
        .where(eq(serverCatalogInstances.serverCatalogId, serverCatalogId))
        .all()
        .map((r) => r.instanceId);
    }

    const base = and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME));
    const rows = db
      .select(instanceColumns)
      .from(instances)
      .where(instanceIds === null ? base : and(base, inArray(instances.id, instanceIds)))
      .orderBy(asc(instances.sortOrder), asc(instances.name))
      .all();
    return reply.send(rows);
  });

  app.get<{ Params: { id: string }; Querystring: { game?: string } }>("/api/instances/:id/bosses", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const instance = findInstance(request.params.id, game);
    if (!instance) {
      return reply.status(404).send({ error: "instance_not_found" });
    }

    const rows = db
      .select({ id: bosses.id, name: bosses.name, nameEn: bosses.nameEn, slug: bosses.slug, isSolo: bosses.isSolo })
      .from(bosses)
      .where(and(eq(bosses.instanceId, instance.id), eq(bosses.isTrashMob, false)))
      .orderBy(asc(bosses.name))
      .all();
    return reply.send(rows);
  });
}
