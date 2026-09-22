import { and, eq, max, ne } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounters, instances } from "../db/schema.js";
import { env } from "../env.js";
import { GAMES, UNASSIGNED_INSTANCE_NAME } from "../constants.js";
import { cached } from "../seo/cache.js";
import { escapeHtml } from "../seo/html.js";

const SITEMAP_TTL_MS = 600_000;

// Crawler plumbing. Player profiles, single encounters and search are deliberately absent from the
// sitemap and blocked in robots.txt: thousands of thin, near-duplicate pages would eat crawl budget
// without ranking for anything, and players don't expect their character to be a Google result.
export async function seoRoutes(app: FastifyInstance) {
  app.get("/robots.txt", async (_request, reply) => {
    reply.type("text/plain; charset=utf-8").header("Cache-Control", "public, max-age=3600");
    return [
      "User-agent: *",
      "Allow: /",
      "Disallow: /api/",
      "Disallow: /*/search",
      "Disallow: /*/players/",
      "Disallow: /*/encounters/",
      "Disallow: /*/participants/",
      "",
      `Sitemap: ${env.BASE_URL}/sitemap.xml`,
      "",
    ].join("\n");
  });

  app.get("/sitemap.xml", async (_request, reply) => {
    reply.type("application/xml; charset=utf-8").header("Cache-Control", "public, max-age=600");
    return cached("sitemap", SITEMAP_TTL_MS, buildSitemap);
  });
}

function buildSitemap(): string {
  const urls: { path: string; lastmod?: string }[] = [{ path: "/" }, { path: "/download" }];

  for (const game of GAMES) {
    const instanceRows = db
      .select({ id: instances.id, slug: instances.slug })
      .from(instances)
      .where(and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME)))
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
      .where(and(eq(instances.game, game), ne(instances.name, UNASSIGNED_INSTANCE_NAME), eq(bosses.isTrashMob, false)))
      .groupBy(bosses.id)
      .all();

    const instanceLastmod = new Map<number, string>();
    for (const b of bossRows) {
      if (b.lastFight && (instanceLastmod.get(b.instanceId) ?? "") < b.lastFight) {
        instanceLastmod.set(b.instanceId, b.lastFight);
      }
    }

    urls.push({ path: `/${game}/instances` });
    for (const i of instanceRows) {
      if (i.slug) {
        urls.push({ path: `/${game}/instances/${i.slug}`, lastmod: instanceLastmod.get(i.id) });
      }
    }
    for (const b of bossRows) {
      if (b.slug) {
        urls.push({ path: `/${game}/bosses/${b.slug}`, lastmod: b.lastFight ?? undefined });
      }
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
