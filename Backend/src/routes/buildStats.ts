import type { FastifyInstance } from "fastify";
import { eq } from "drizzle-orm";
import { db, sqlite } from "../db/client.js";
import { players } from "../db/schema.js";
import { parseIdOrSlug } from "../seo/slug.js";
import { AION2_CLASS_BY_ID } from "../constants.js";
import { GROUPS, METRICS, MIN_SAMPLE, TOPICS, compareToGroup, optimalCharacter, readAnswer, type Group, type Metric, type Topic } from "../buildStats.js";

/**
 * "What do the best use?" (buildStats.ts). Only the stored group results are read: no single character is ever returned, cells with fewer than three characters are not
 * stored, and a question with fewer than ten behind it comes back as "too little data" without numbers. The ids are the game's; the page resolves names from the game data.
 */
const CACHE = "public, max-age=300";

export async function buildStatsRoutes(app: FastifyInstance) {
  /** What exists: classes, groups, ranking measures and, per class and group, how many characters are behind it and when it was built. */
  app.get("/api/build-stats", async (_request, reply) => {
    const meta = sqlite
      .prepare("select class_id as classId, metric, grp as \"group\", size, built_at as builtAt from build_stats_meta order by class_id, metric, grp")
      .all();
    void reply.header("Cache-Control", CACHE);
    return {
      classes: Object.entries(AION2_CLASS_BY_ID).map(([id, name]) => ({ classId: Number(id), name })),
      groups: GROUPS,
      metrics: METRICS,
      topics: TOPICS,
      minSample: MIN_SAMPLE,
      meta,
    };
  });

  const parse = (params: { classId: string }, query: { group?: string; metric?: string; topic?: string }) => {
    const classId = /^\d$/.test(params.classId) ? Number(params.classId) : 0;
    const group = (query.group ?? "top25") as Group;
    const metric = (query.metric ?? "power") as Metric;
    const topic = query.topic as Topic | undefined;
    const valid = AION2_CLASS_BY_ID[classId] !== undefined && GROUPS.includes(group) && METRICS.includes(metric) && (topic === undefined || TOPICS.includes(topic));
    return valid ? { classId, group, metric, topic } : null;
  };

  /** The answers of one class for one group (optionally one topic). */
  app.get<{ Params: { classId: string }; Querystring: { group?: string; metric?: string; topic?: string } }>("/api/build-stats/:classId", async (request, reply) => {
    const q = parse(request.params, request.query);
    if (!q) {
      return reply.status(400).send({ error: "bad_request" });
    }
    const answer = readAnswer(sqlite, q.classId, q.group, q.metric, q.topic);
    if (!answer) {
      return reply.status(404).send({ error: "not_built_yet" });
    }
    void reply.header("Cache-Control", CACHE);
    return answer;
  });

  /** The "optimal character" of one class and group: most frequent choice per selection, median per number, the most frequent real build per board. */
  app.get<{ Params: { classId: string }; Querystring: { group?: string; metric?: string } }>("/api/build-stats/:classId/optimal", async (request, reply) => {
    const q = parse(request.params, request.query);
    if (!q) {
      return reply.status(400).send({ error: "bad_request" });
    }
    const answer = readAnswer(sqlite, q.classId, q.group, q.metric);
    if (!answer) {
      return reply.status(404).send({ error: "not_built_yet" });
    }
    void reply.header("Cache-Control", CACHE);
    return optimalCharacter(answer);
  });

  /** One character against the Top N (or the rest) of its own class: where it differs in skill levels, stones, gear and Daevanion boards. */
  app.get<{ Params: { id: string }; Querystring: { group?: string; metric?: string } }>("/api/players/:id/build-compare", async (request, reply) => {
    const key = parseIdOrSlug(request.params.id);
    const group = (request.query.group ?? "top10") as Group;
    const metric = (request.query.metric ?? "power") as Metric;
    if (!key || !GROUPS.includes(group) || !METRICS.includes(metric)) {
      return reply.status(400).send({ error: "bad_request" });
    }
    const player = db
      .select({ id: players.id, deletedAt: players.deletedAt })
      .from(players)
      .where("id" in key ? eq(players.id, key.id) : eq(players.slug, key.slug))
      .get();
    if (!player || player.deletedAt !== null) {
      return reply.status(404).send({ error: "player_not_found" });
    }
    const comparison = compareToGroup(sqlite, player.id, group, metric);
    if (!comparison) {
      return reply.status(404).send({ error: "no_comparison" });
    }
    void reply.header("Cache-Control", "public, max-age=300");
    return comparison;
  });
}
