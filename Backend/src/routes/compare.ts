import { and, asc, avg, count, desc, eq, max, ne } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounterSkillUsage, encounters, instances, players, servers } from "../db/schema.js";
import { findBoss } from "./bosses.js";
import { gameFromQuery } from "./instances.js";

// The comparison pages (runs side by side, two players on one boss) are built from the pickers'
// lists below plus /api/compare/players. Comparing two runs needs nothing new - the page loads
// /api/encounters/:id twice.

// Pets/summons are stored as participants with className "?" (see encounterPage in seo/pages.ts):
// never a person to compare with.
const REAL_PLAYER = ne(encounterParticipants.className, "?");

function parseId(raw: unknown): number | null {
  const n = Number(raw);
  return Number.isInteger(n) && n > 0 ? n : null;
}

/** One player's own results on one boss: how many fights, their best/average, and the best fight itself. */
function playerOnBoss(playerId: number, bossId: number) {
  const player = db
    .select({ id: players.id, name: players.name, guild: players.guild, serverName: servers.displayName })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where(eq(players.id, playerId))
    .get();
  if (!player) {
    return null;
  }

  const scope = and(eq(encounterParticipants.playerId, playerId), eq(encounters.bossId, bossId), REAL_PLAYER);
  const totals = db
    .select({
      runs: count(),
      avgIdps: avg(encounterParticipants.idps),
      avgDamage: avg(encounterParticipants.totalDamage),
      avgCrit: avg(encounterParticipants.critRatePercent),
      avgHps: avg(encounterParticipants.hps),
    })
    .from(encounterParticipants)
    .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .where(scope)
    .get();
  if (!totals || totals.runs === 0) {
    return null;
  }

  const best = db
    .select({
      participantId: encounterParticipants.id,
      encounterId: encounters.id,
      startedAt: encounters.startedAt,
      durationSeconds: encounters.durationSeconds,
      groupIDps: encounters.groupIDps,
      className: encounterParticipants.className,
      faction: encounterParticipants.faction,
      totalDamage: encounterParticipants.totalDamage,
      dps: encounterParticipants.dps,
      idps: encounterParticipants.idps,
      totalHealing: encounterParticipants.totalHealing,
      hps: encounterParticipants.hps,
      critRatePercent: encounterParticipants.critRatePercent,
    })
    .from(encounterParticipants)
    .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .where(scope)
    .orderBy(desc(encounterParticipants.idps), desc(encounters.startedAt))
    .get()!;

  const skills = db
    .select({
      skillName: encounterSkillUsage.skillName,
      hits: encounterSkillUsage.hits,
      critHits: encounterSkillUsage.critHits,
      totalDamage: encounterSkillUsage.totalDamage,
      maxHit: encounterSkillUsage.maxHit,
    })
    .from(encounterSkillUsage)
    .where(and(eq(encounterSkillUsage.participantId, best.participantId), eq(encounterSkillUsage.isHeal, false)))
    .orderBy(desc(encounterSkillUsage.totalDamage))
    .all();

  return {
    player,
    runs: totals.runs,
    averages: {
      idps: Number(totals.avgIdps ?? 0),
      totalDamage: Number(totals.avgDamage ?? 0),
      critRatePercent: Number(totals.avgCrit ?? 0),
      hps: Number(totals.avgHps ?? 0),
    },
    best,
    skills,
  };
}

export async function compareRoutes(app: FastifyInstance) {
  // Picker for "compare with another run": the boss's most recent fights, newest first.
  app.get<{ Params: { id: string }; Querystring: { game?: string; limit?: string } }>("/api/bosses/:id/runs", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const found = findBoss(request.params.id, game);
    if (!found) {
      return reply.status(404).send({ error: "boss_not_found" });
    }
    const limit = Math.min(Math.max(Number(request.query.limit ?? 40) || 40, 1), 100);

    const rows = db
      .select({
        encounterId: encounters.id,
        startedAt: encounters.startedAt,
        durationSeconds: encounters.durationSeconds,
        groupIDps: encounters.groupIDps,
        serverName: servers.displayName,
      })
      .from(encounters)
      .leftJoin(servers, eq(encounters.serverId, servers.id))
      .where(eq(encounters.bossId, found.boss.id))
      .orderBy(desc(encounters.startedAt))
      .limit(limit)
      .all();

    const withTop = rows.map((row) => {
      const top = db
        .select({ playerName: players.name, className: encounterParticipants.className })
        .from(encounterParticipants)
        .innerJoin(players, eq(encounterParticipants.playerId, players.id))
        .where(and(eq(encounterParticipants.encounterId, row.encounterId), REAL_PLAYER))
        .orderBy(desc(encounterParticipants.totalDamage))
        .get();
      const size = db
        .select({ n: count() })
        .from(encounterParticipants)
        .where(and(eq(encounterParticipants.encounterId, row.encounterId), REAL_PLAYER))
        .get();
      return { ...row, topPlayerName: top?.playerName ?? null, topPlayerClassName: top?.className ?? null, playerCount: size?.n ?? 0 };
    });
    return reply.send({ boss: { id: found.boss.id, name: found.boss.name, nameEn: found.boss.nameEn, slug: found.boss.slug }, runs: withTop });
  });

  // Step 1 of the player comparison: every boss this player has data for.
  app.get<{ Params: { id: string } }>("/api/players/:id/bosses", async (request, reply) => {
    const playerId = parseId(request.params.id);
    if (playerId === null) {
      return reply.status(400).send({ error: "invalid_player_id" });
    }
    const rows = db
      .select({
        bossId: bosses.id,
        bossName: bosses.name,
        bossNameEn: bosses.nameEn,
        bossSlug: bosses.slug,
        instanceName: instances.name,
        instanceNameEn: instances.nameEn,
        runs: count(),
        bestIdps: max(encounterParticipants.idps),
      })
      .from(encounterParticipants)
      .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
      .innerJoin(bosses, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(and(eq(encounterParticipants.playerId, playerId), REAL_PLAYER))
      .groupBy(bosses.id)
      .orderBy(asc(instances.name), asc(bosses.name))
      .all();
    return reply.send(rows);
  });

  // Step 2: the other players who have data for that boss (the first player excluded).
  app.get<{ Params: { id: string }; Querystring: { game?: string; exclude?: string } }>("/api/bosses/:id/players", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const found = findBoss(request.params.id, game);
    if (!found) {
      return reply.status(404).send({ error: "boss_not_found" });
    }
    const exclude = parseId(request.query.exclude);

    const rows = db
      .select({
        playerId: players.id,
        name: players.name,
        guild: players.guild,
        serverName: servers.displayName,
        runs: count(),
        bestIdps: max(encounterParticipants.idps),
      })
      .from(encounterParticipants)
      .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
      .innerJoin(players, eq(encounterParticipants.playerId, players.id))
      .leftJoin(servers, eq(players.serverId, servers.id))
      .where(and(eq(encounters.bossId, found.boss.id), REAL_PLAYER, exclude === null ? undefined : ne(players.id, exclude)))
      .groupBy(players.id)
      .orderBy(desc(max(encounterParticipants.idps)))
      .limit(300)
      .all();

    // The class a player used for their best fight - one extra lookup per row would be 300 queries,
    // so the (small) page of class names is resolved from the most recent row per player instead.
    const classes = new Map<number, string>();
    for (const row of db
      .select({ playerId: encounterParticipants.playerId, className: encounterParticipants.className })
      .from(encounterParticipants)
      .innerJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
      .where(and(eq(encounters.bossId, found.boss.id), REAL_PLAYER))
      .orderBy(asc(encounters.startedAt))
      .all()) {
      classes.set(row.playerId, row.className);
    }
    return reply.send(rows.map((r) => ({ ...r, className: classes.get(r.playerId) ?? null })));
  });

  // The comparison itself: both players' results on one boss, best fight and averages.
  app.get<{ Querystring: { a?: string; b?: string; boss?: string; game?: string } }>("/api/compare/players", async (request, reply) => {
    const game = gameFromQuery(request.query.game, reply);
    if (game === null) {
      return;
    }
    const a = parseId(request.query.a);
    const b = parseId(request.query.b);
    if (a === null || b === null || request.query.boss === undefined) {
      return reply.status(400).send({ error: "missing_parameters" });
    }
    const found = findBoss(request.query.boss, game);
    if (!found) {
      return reply.status(404).send({ error: "boss_not_found" });
    }

    const left = playerOnBoss(a, found.boss.id);
    const right = playerOnBoss(b, found.boss.id);
    if (!left || !right) {
      return reply.status(404).send({ error: "no_data_for_boss" });
    }
    return reply.send({
      boss: { id: found.boss.id, name: found.boss.name, nameEn: found.boss.nameEn, slug: found.boss.slug, instanceName: found.instanceName },
      a: left,
      b: right,
    });
  });
}
