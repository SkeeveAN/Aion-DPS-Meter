import { and, eq, like, max, ne } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounterParticipants, encounters, instances, players, servers } from "../db/schema.js";
import { env } from "../env.js";
import { DEFAULT_GAME, UNASSIGNED_INSTANCE_NAME } from "../constants.js";
import { cached } from "../seo/cache.js";
import { escapeHtml } from "../seo/html.js";
import { encounterOgImage } from "../seo/ogEncounter.js";

const SITEMAP_TTL_MS = 600_000;

// Crawler plumbing. Single encounters, comparisons and search are deliberately absent from the
// sitemap and blocked in robots.txt: thousands of thin, near-duplicate pages would eat crawl budget
// without ranking for anything. Player profiles are open to search engines (per the user) and listed
// in the sitemap.
export async function seoRoutes(app: FastifyInstance) {
  app.get("/robots.txt", async (_request, reply) => {
    reply.type("text/plain; charset=utf-8").header("Cache-Control", "public, max-age=3600");
    return [
      "User-agent: *",
      "Allow: /",
      "Disallow: /api/",
      "Disallow: /search",
      "Disallow: /compare/",
      "Disallow: /encounters/",
      "Disallow: /participants/",
      "",
      `Sitemap: ${env.BASE_URL}/sitemap.xml`,
      "",
    ].join("\n");
  });

  // Link-preview picture of one fight (og:image of /encounters/:id). ?lang= only changes the boss and
  // instance names and the number format - the rest of the picture is icons and numbers.
  app.get<{ Params: { file: string }; Querystring: { lang?: string } }>("/og/encounters/:file", async (request, reply) => {
    const match = /^(\d+)\.png$/.exec(request.params.file);
    const png = match ? encounterOgImage(DEFAULT_GAME, match[1], request.query.lang) : null;
    if (!png) {
      return reply.status(404).send({ error: "not_found" });
    }
    reply.type("image/png").header("Cache-Control", "public, max-age=3600");
    return png;
  });

  app.get("/sitemap.xml", async (_request, reply) => {
    reply.type("application/xml; charset=utf-8").header("Cache-Control", "public, max-age=600");
    return cached("sitemap", SITEMAP_TTL_MS, buildSitemap);
  });
}

function buildSitemap(): string {
  const urls: { path: string; lastmod?: string }[] = [{ path: "/" }, { path: "/download" }];

  for (const game of [DEFAULT_GAME]) {
    const instanceRows = db
      .select({ id: instances.id, slug: instances.slug })
      .from(instances)
      .where(and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME), eq(instances.hidden, false)))
      .all();
    if (instanceRows.length === 0) {
      continue;
    }

    // Latest fight per boss doubles as lastmod for the boss page, and the newest of those for its
    // instance page. Bosses without a slug (none after the backfill) or hidden trash mobs are skipped.
    const bossRows = db
      .select({ slug: bosses.slug, instanceId: bosses.instanceId, lastFight: max(encounters.startedAt) })
      .from(bosses)
      .leftJoin(encounters, eq(encounters.bossId, bosses.id))
      .innerJoin(instances, eq(bosses.instanceId, instances.id))
      .where(and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME), eq(instances.hidden, false), eq(bosses.isTrashMob, false)))
      .groupBy(bosses.id)
      .all();

    const instanceLastmod = new Map<number, string>();
    for (const b of bossRows) {
      if (b.lastFight && (instanceLastmod.get(b.instanceId) ?? "") < b.lastFight) {
        instanceLastmod.set(b.instanceId, b.lastFight);
      }
    }

    urls.push({ path: `/instances` }, { path: `/instances/expedition` }, { path: `/instances/expedition/hard` }, { path: `/instances/nightmare` }, { path: `/instances/ascension` }, { path: `/instances/transcendence` });
    for (const i of instanceRows) {
      if (i.slug) {
        urls.push({ path: `/instances/${i.slug}`, lastmod: instanceLastmod.get(i.id) });
      }
    }
    for (const b of bossRows) {
      if (b.slug) {
        urls.push({ path: `/bosses/${b.slug}`, lastmod: b.lastFight ?? undefined });
      }
    }
  }

  // Boss pages of a difficulty step (Nightmare level ...) that already have fights.
  const modeRows = db
    .select({ slug: bosses.slug, mode: encounters.mode, lastFight: max(encounters.startedAt) })
    .from(encounters)
    .innerJoin(bosses, eq(encounters.bossId, bosses.id))
    .innerJoin(instances, eq(bosses.instanceId, instances.id))
    .where(and(eq(instances.hidden, false), ne(encounters.mode, "")))
    .groupBy(bosses.id, encounters.mode)
    .all();
  for (const r of modeRows) {
    if (r.slug) {
      urls.push({ path: `/bosses/${r.slug}?mode=${r.mode}`, lastmod: r.lastFight ?? undefined });
    }
  }

  // Every Aion 2 character; lastmod is its latest fight, if it has one.
  const playerRows = db
    .select({ slug: players.slug, lastFight: max(encounters.startedAt) })
    .from(players)
    .innerJoin(servers, eq(players.serverId, servers.id))
    .leftJoin(encounterParticipants, and(eq(encounterParticipants.playerId, players.id), ne(encounterParticipants.className, "?")))
    .leftJoin(encounters, eq(encounterParticipants.encounterId, encounters.id))
    .where(like(servers.fingerprint, "aion2:%"))
    .groupBy(players.id)
    .all();
  for (const p of playerRows) {
    if (p.slug) {
      urls.push({ path: `/players/${p.slug}`, lastmod: p.lastFight ?? undefined });
    }
  }

  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    ...urls.map(
      (u) =>
        `  <url><loc>${escapeHtml(env.BASE_URL + u.path)}</loc>${u.lastmod ? `<lastmod>${u.lastmod.slice(0, 10)}</lastmod>` : ""}</url>`,
    ),
    "</urlset>",
    "",
  ].join("\n");
}
