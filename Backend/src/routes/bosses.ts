import { and, asc, count, desc, eq, or } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bossMechanics, bossNpcIds, bosses, encounterParticipants, encounters, instances, players, serverCatalog, servers } from "../db/schema.js";
import { topBuffsByParticipant, type TopBuff } from "../skills/topBuffs.js";
import { parseIdOrSlug } from "../seo/slug.js";
import { gameFromQuery } from "./instances.js";
import type { Game } from "../constants.js";

const TOP_N = 10;

/** Boss by numeric id, or by slug within a game (the game lives on the instance row). */
export function findBoss(idOrSlug: string, game: Game) {
  const key = parseIdOrSlug(idOrSlug);
  if (key === null) {
    return null;
  }
  return (
    db
      .select({
        boss: bosses,
        instanceSlug: instances.slug,
        instanceName: instances.name,
        instanceNameEn: instances.nameEn,
        game: instances.game,
      })
      .from(bosses)
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where("id" in key ? eq(bosses.id, key.id) : and(eq(bosses.slug, key.slug), eq(instances.game, game)))
      .get() ?? null
  );
}

/**
 * Every server that has at least one encounter for this boss, busiest first - what a page offers
 * as server tabs, and where a request without a chosen server defaults to. The catalog slug comes
 * via the same displayName join servers.ts uses; null for a server whose upload name matches no
 * catalog entry.
 */
export function serversWithEncounters(bossId: number) {
  return db
    .select({
      id: servers.id,
      name: servers.displayName,
      slug: serverCatalog.slug,
      encounterCount: count(encounters.id),
    })
    .from(encounters)
    .innerJoin(servers, eq(encounters.serverId, servers.id))
    .leftJoin(serverCatalog, eq(serverCatalog.name, servers.displayName))
    .where(eq(encounters.bossId, bossId))
    .groupBy(servers.id)
    .orderBy(desc(count(encounters.id)))
    .all();
}

export function topGroups(bossId: number, serverId: number | null) {
  const groups = db
    .select({
      encounterId: encounters.id,
      startedAt: encounters.startedAt,
      durationSeconds: encounters.durationSeconds,
      groupIDps: encounters.groupIDps,
      mergedUploadCount: encounters.mergedUploadCount,
    })
    .from(encounters)
    .where(serverId === null ? eq(encounters.bossId, bossId) : and(eq(encounters.bossId, bossId), eq(encounters.serverId, serverId)))
    .orderBy(desc(encounters.groupIDps))
    .limit(TOP_N)
    .all();

  // roster carries every member's name (per the user: a group row must show everyone, not
  // just one "face of this run") - representative (highest damage dealer, roster[0] once
  // sorted by damage) is kept alongside it only to pick a face/class-icon for the row.
  const groupsWithRoster = groups.map((group) => {
    const roster = db
      .select({
        participantId: encounterParticipants.id,
        playerName: players.name,
        serverName: servers.displayName,
        className: encounterParticipants.className,
        faction: encounterParticipants.faction,
        totalDamage: encounterParticipants.totalDamage,
        totalHealing: encounterParticipants.totalHealing,
        damageTaken: encounterParticipants.damageTaken,
      })
      .from(encounterParticipants)
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .leftJoin(servers, eq(players.serverId, servers.id))
      .where(eq(encounterParticipants.encounterId, group.encounterId))
      .orderBy(desc(encounterParticipants.totalDamage))
      .all();

    return {
      ...group,
      playerCount: roster.length,
      totalDamage: roster.reduce((sum, p) => sum + p.totalDamage, 0),
      totalHealing: roster.reduce((sum, p) => sum + p.totalHealing, 0),
      roster,
      representative: roster[0] ?? null,
    };
  });

  // Per the user: a group row's Buffs must reflect the whole group's reinforcements, not just
  // its top damage dealer's - summed by skill across every member, then capped the same way a
  // single participant's own topBuffs already is.
  const buffsByParticipant = topBuffsByParticipant(groupsWithRoster.flatMap((g) => g.roster.map((p) => p.participantId)));
  return groupsWithRoster.map((g) => {
    const castsBySkill = new Map<string, TopBuff>();
    for (const p of g.roster) {
      for (const buff of buffsByParticipant.get(p.participantId) ?? []) {
        const existing = castsBySkill.get(buff.skillName);
        castsBySkill.set(buff.skillName, { ...buff, casts: (existing?.casts ?? 0) + buff.casts });
      }
    }
    const groupBuffs = [...castsBySkill.values()].sort((a, b) => b.casts - a.casts).slice(0, 8);
    return { ...g, groupBuffs };
  });
}

/** "Top 10 per class" for a solo target - fetch once, group and cap in JS (a few thousand rows at most). */
export function topByClass(bossId: number, serverId: number | null) {
  const allParticipants = db
    .select({
      participantId: encounterParticipants.id,
      encounterId: encounterParticipants.encounterId,
      playerName: players.name,
      serverName: servers.displayName,
      className: encounterParticipants.className,
      faction: encounterParticipants.faction,
      idps: encounterParticipants.idps,
      totalDamage: encounterParticipants.totalDamage,
      totalHealing: encounterParticipants.totalHealing,
    })
    .from(encounterParticipants)
    .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .innerJoin(players, eq(encounterParticipants.playerId, players.id))
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where(serverId === null ? eq(encounters.bossId, bossId) : and(eq(encounters.bossId, bossId), eq(encounters.serverId, serverId)))
    .all();

  const byClass = new Map<string, typeof allParticipants>();
  for (const p of allParticipants) {
    const list = byClass.get(p.className) ?? [];
    list.push(p);
    byClass.set(p.className, list);
  }

  const result: Record<string, (typeof allParticipants[number] & { topBuffs: TopBuff[] })[]> = {};
  const capped = [...byClass.entries()].map(([className, list]) => [className, list.sort((a, b) => b.idps - a.idps).slice(0, TOP_N)] as const);
  const topBuffs = topBuffsByParticipant(capped.flatMap(([, list]) => list.map((p) => p.participantId)));
  for (const [className, list] of capped) {
    result[className] = list.map((p) => ({ ...p, topBuffs: topBuffs.get(p.participantId) ?? [] }));
  }
  return result;
}

/** Wipe-mechanics reference for a boss: its own rows plus the dungeon-wide rules of its instance. */
export function mechanicsFor(bossId: number, instanceId: number) {
  const rows = db
    .select()
    .from(bossMechanics)
    .where(or(eq(bossMechanics.bossId, bossId), eq(bossMechanics.instanceId, instanceId)))
    .orderBy(asc(bossMechanics.sortOrder))
    .all();
  return {
    instanceWide: rows.filter((r) => r.bossId === null),
    mechanics: rows.filter((r) => r.bossId === bossId),
  };
}

export function npcIdsFor(bossId: number): number[] {
  return db.select({ npcId: bossNpcIds.npcId }).from(bossNpcIds).where(eq(bossNpcIds.bossId, bossId)).all().map((r) => r.npcId);
}

/**
 * Picks the server a leaderboard shows: an explicit serverId, else an explicit catalog slug
 * (`?server=origin-aion`, the URL form), else the busiest server for this boss. Per the user,
 * different servers' gear standards are incomparable, so there is never an "all servers" merge -
 * only ever exactly one server's ranking, with the others offered as alternatives.
 */
export function selectServer(
  list: ReturnType<typeof serversWithEncounters>,
  query: { serverId?: string; server?: string },
): { id: number } | null | "invalid" {
  if (query.serverId !== undefined) {
    const id = Number(query.serverId);
    return Number.isInteger(id) ? { id } : "invalid";
  }
  if (query.server !== undefined) {
    const hit = list.find((s) => s.slug === query.server);
    return hit ? { id: hit.id } : null;
  }
  return list[0] ? { id: list[0].id } : null;
}

export async function bossRoutes(app: FastifyInstance) {
  app.get<{ Params: { id: string }; Querystring: { serverId?: string; server?: string; game?: string } }>(
    "/api/bosses/:id/leaderboard",
    async (request, reply) => {
      const game = gameFromQuery(request.query.game, reply);
      if (game === null) {
        return;
      }
      const found = findBoss(request.params.id, game);
      if (!found) {
        return reply.status(404).send({ error: "boss_not_found" });
      }
      const boss = found.boss;

      const bossResponse = {
        id: boss.id,
        name: boss.name,
        nameEn: boss.nameEn,
        slug: boss.slug,
        game: found.game,
        instanceId: boss.instanceId,
        instanceSlug: found.instanceSlug,
        instanceName: found.instanceName,
        instanceNameEn: found.instanceNameEn,
        isSolo: boss.isSolo,
        lootRules: boss.lootRules,
        hasMechanics: mechanicsFor(boss.id, boss.instanceId).mechanics.length > 0,
      };
      const serverList = serversWithEncounters(boss.id);
      // Aion 2 runs official, same-standard servers and its groups can span them, so its
      // ranking is one list across every server unless one is asked for explicitly. Classic
      // Aion's private servers are never merged (see selectServer).
      const explicitServer = request.query.serverId !== undefined || request.query.server !== undefined;
      const combined = found.game === "aion2" && !explicitServer;
      const selected = combined ? null : selectServer(serverList, request.query);
      if (selected === "invalid") {
        return reply.status(400).send({ error: "missing_or_invalid_server_id" });
      }

      if (selected === null && !combined) {
        return reply.send({ boss: bossResponse, servers: serverList, selectedServerId: null, combined: false, topGroups: [], topByClass: {} });
      }
      const scope = combined ? null : selected!.id;

      // Per the user: a real group fight and a solo practice target (e.g. Training Dummy) rank
      // completely differently - one boss is never both, so only the query the page actually
      // needs runs. isSolo is manually curated (see README), same pattern as isTrashMob.
      if (boss.isSolo) {
        return reply.send({ boss: bossResponse, servers: serverList, selectedServerId: scope, combined, topGroups: [], topByClass: topByClass(boss.id, scope) });
      }
      return reply.send({ boss: bossResponse, servers: serverList, selectedServerId: scope, combined, topGroups: topGroups(boss.id, scope), topByClass: {} });
    },
  );

  // Curated wipe-mechanics reference (see schema.ts bossMechanics) - facts derived from game data
  // plus our own prose; a row with an empty action is still "being written".
  app.get<{ Params: { id: string }; Querystring: { game?: string } }>("/api/bosses/:id/mechanics", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const found = findBoss(request.params.id, game);
    if (!found) {
      return reply.status(404).send({ error: "boss_not_found" });
    }
    const { instanceWide, mechanics } = mechanicsFor(found.boss.id, found.boss.instanceId);
    return reply.send({
      boss: {
        id: found.boss.id,
        name: found.boss.name,
        nameEn: found.boss.nameEn,
        slug: found.boss.slug,
        instanceSlug: found.instanceSlug,
        instanceName: found.instanceName,
        instanceNameEn: found.instanceNameEn,
        npcIds: npcIdsFor(found.boss.id),
        source: found.boss.source,
      },
      instanceWide,
      mechanics,
    });
  });
}
