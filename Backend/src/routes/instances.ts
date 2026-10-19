import { instanceStatistics } from "../instanceStats.js";
import { ne, eq, and, asc, count, desc, inArray } from "drizzle-orm";
import type { FastifyInstance, FastifyReply } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounters, instances, serverCatalogInstances } from "../db/schema.js";
import { CATEGORY_MODES, DEFAULT_GAME, isGame, UNASSIGNED_INSTANCE_NAME, type Game } from "../constants.js";
import { parseIdOrSlug } from "../seo/slug.js";
import { parseGearRange } from "../gearFilter.js";
import { selectServer, serversWithEncountersForBossIds, statsForBossIds } from "./bosses.js";

/** `?game=` is optional everywhere - absent means aion2, the only game served. */
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
  variant: instances.variant,
  hidden: instances.hidden,
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

    const base = and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME), eq(instances.hidden, false));
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

  // The statistics of a dungeon: per boss its fight numbers and class medians, plus the class share of the whole dungeon (the
  // dungeon's "Statistics" page). Combined across the servers; a difficulty step (mode) narrows it like on the boss pages.
  app.get<{ Params: { id: string }; Querystring: { game?: string; mode?: string; gear?: string } }>("/api/instances/:id/statistics", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const instance = findInstance(request.params.id, game);
    if (!instance) {
      return reply.status(404).send({ error: "instance_not_found" });
    }
    const mode = request.query.mode === undefined ? null : request.query.mode === "unknown" ? "" : request.query.mode;
    return reply.send({ instance: { id: instance.id, slug: instance.slug, name: instance.name, nameEn: instance.nameEn, category: instance.category }, ...instanceStatistics(instance.id, null, mode, parseGearRange(request.query.gear)) });
  });

  // Best group iDPS / average kill time / run count across every one of this instance's real
  // bosses (trash mobs excluded, same filter as /bosses above) - powers the instance hero's stat
  // pills and, later, an instance-level "Statistiken" tab. Server scoping mirrors a boss
  // leaderboard exactly (see bosses.ts selectServer/statsForBossIds): never merged across
  // classic-Aion servers, combined by default only for Aion 2.
  app.get<{ Params: { id: string }; Querystring: { game?: string; serverId?: string; server?: string; mode?: string; gear?: string } }>(
    "/api/instances/:id/stats",
    async (request, reply) => {
      const game = gameFromQuery(request.query.game, reply);
      if (game === null) {
        return;
      }
      const instance = findInstance(request.params.id, game);
      if (!instance) {
        return reply.status(404).send({ error: "instance_not_found" });
      }

      const bossIds = db
        .select({ id: bosses.id })
        .from(bosses)
        .where(and(eq(bosses.instanceId, instance.id), eq(bosses.isTrashMob, false)))
        .all()
        .map((r) => r.id);

      const serverList = serversWithEncountersForBossIds(bossIds);
      const explicitServer = request.query.serverId !== undefined || request.query.server !== undefined;
      const combined = game === "aion2" && !explicitServer;
      const selected = combined ? null : selectServer(serverList, request.query);
      if (selected === "invalid") {
        return reply.status(400).send({ error: "missing_or_invalid_server_id" });
      }

      if (selected === null && !combined) {
        return reply.send({
          instanceId: instance.id,
          servers: serverList,
          selectedServerId: null,
          combined: false,
          runCount: 0,
          bestIdps: null,
          avgDurationSeconds: null,
        });
      }
      const scope = combined ? null : selected!.id;
      // Difficulty steps of this instance's category (Transcendence stage, Ascension step, ...) with their run counts, and the
      // chosen one narrowing the figures; an unknown or missing step means every step together.
      const stepNames = CATEGORY_MODES[instance.category ?? ""] ?? [];
      const stepRuns = new Map(
        bossIds.length === 0
          ? []
          : db
              .select({ mode: encounters.mode, runs: count(encounters.id) })
              .from(encounters)
              .where(inArray(encounters.bossId, bossIds))
              .groupBy(encounters.mode)
              .all()
              .map((r) => [r.mode, r.runs] as const),
      );
      const modes = stepNames.length === 0 ? [] : stepNames.map((mode) => ({ mode, runs: stepRuns.get(mode) ?? 0 }));
      const selectedMode = stepNames.includes(request.query.mode ?? "") ? request.query.mode! : null;
      const stats = statsForBossIds(bossIds, scope, selectedMode, parseGearRange(request.query.gear));
      return reply.send({ instanceId: instance.id, servers: serverList, selectedServerId: scope, combined, modes, selectedMode, ...stats });
    },
  );

  // "Top instances" by real run count (aiondps_design_pack_v1 startseite brief's "Featured
  // Instances", replaced with an actual ranking rather than just the first N by sortOrder) - summed
  // across every server, unlike a DPS leaderboard: a run count is just activity volume, not a
  // gear-standard comparison, so classic-Aion servers being incomparable for DPS doesn't apply here.
  app.get<{ Querystring: { game?: string; limit?: string } }>("/api/instances/top", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const limit = Math.min(Math.max(Number(request.query.limit ?? 8) || 8, 1), 50);

    const rows = db
      .select({ ...instanceColumns, runCount: count(encounters.id) })
      .from(instances)
      .innerJoin(bosses, eq(bosses.instanceId, instances.id))
      .innerJoin(encounters, eq(encounters.bossId, bosses.id))
      .where(and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME), eq(instances.hidden, false)))
      .groupBy(instances.id)
      .orderBy(desc(count(encounters.id)))
      .limit(limit)
      .all();

    return reply.send(rows);
  });
}
