import { timingSafeEqual } from "node:crypto";
import type { FastifyInstance, FastifyReply } from "fastify";
import { env } from "../env.js";
import { metricsText, privateStats, publicStats } from "../stats/collect.js";
import { cached } from "../seo/cache.js";

function same(a: string, b: string): boolean {
  const x = Buffer.from(a);
  const y = Buffer.from(b);
  return x.length === y.length && timingSafeEqual(x, y);
}

const hidden = (reply: FastifyReply) => reply.status(404).send({ error: "not_found" });

export async function siteStatsRoutes(app: FastifyInstance) {
  // Public: totals only.
  app.get("/api/stats/overview", async (_request, reply) => {
    reply.header("Cache-Control", "public, max-age=300").type("application/json");
    return cached("stats:public", 300_000, () => JSON.stringify(publicStats()));
  });

  // Private: the secret in the address is the only key; a wrong or unset one looks like any missing page.
  app.get<{ Params: { secret: string } }>("/api/private-stats/:secret", async (request, reply) => {
    if (!env.STATS_SECRET || !same(request.params.secret, env.STATS_SECRET)) {
      return hidden(reply);
    }
    return reply.header("Cache-Control", "no-store").header("X-Robots-Tag", "noindex, nofollow").send(privateStats());
  });

  // Prometheus scrape target, bearer token in the header (never in the address, so it stays out of access logs).
  app.get("/metrics", async (request, reply) => {
    const given = String(request.headers.authorization ?? "").replace(/^Bearer /i, "");
    if (!env.METRICS_TOKEN || !same(given, env.METRICS_TOKEN)) {
      return hidden(reply);
    }
    return reply.header("Cache-Control", "no-store").type("text/plain; version=0.0.4; charset=utf-8").send(metricsText());
  });
}
