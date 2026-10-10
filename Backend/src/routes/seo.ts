import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { and, eq, max, ne } from "drizzle-orm";
import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { bosses, encounters, instances } from "../db/schema.js";
import { env } from "../env.js";
import { DEFAULT_GAME, UNASSIGNED_INSTANCE_NAME } from "../constants.js";
import { cached } from "../seo/cache.js";
import { escapeHtml } from "../seo/html.js";
import { encounterOgImage } from "../seo/ogEncounter.js";
import { playerOgImage } from "../seo/ogPlayer.js";

const SITEMAP_TTL_MS = 600_000;
const CHANGELOG_FILE = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "..", "..", "Web-Frontend", "changelog.json");

// Date of the newest release; the client pages (features, download, changelog) change with it.
function latestReleaseDate(): string | undefined {
  try {
    const entries = JSON.parse(readFileSync(CHANGELOG_FILE, "utf8")) as { date?: string }[];
    return entries.map((e) => e.date ?? "").sort().pop() || undefined;
  } catch {
    return undefined;
  }
}

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
      "Disallow: /download/latest",
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

  // Link-preview picture of one player profile (og:image of /players/:idOrSlug).
  app.get<{ Params: { file: string } }>("/og/players/:file", async (request, reply) => {
    const match = /^([a-z0-9-]+)\.png$/.exec(request.params.file);
    const png = match ? playerOgImage(match[1]) : null;
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
  const release = latestReleaseDate();
  const lastFightAll = db.select({ t: max(encounters.startedAt) }).from(encounters).get()?.t ?? undefined;
  const newest = [release, lastFightAll?.slice(0, 10)].filter(Boolean).sort().pop();
  const urls: { path: string; lastmod?: string }[] = [
    { path: "/", lastmod: newest },
    { path: "/download", lastmod: release },
    { path: "/features", lastmod: release },
    { path: "/changelog", lastmod: release },
    { path: "/stats", lastmod: lastFightAll },
    { path: "/status" },
    { path: "/legions", lastmod: lastFightAll },
  ];

  for (const game of [DEFAULT_GAME]) {
    const instanceRows = db
      .select({ id: instances.id, slug: instances.slug, category: instances.category })
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

    for (const p of ["/instances", "/instances/expedition", "/instances/expedition/hard", "/instances/nightmare", "/instances/ascension", "/instances/transcendence", "/worldbosses"]) {
      urls.push({ path: p, lastmod: newest });
    }
    for (const i of instanceRows) {
      if (i.slug) {
        urls.push({ path: `${i.category === "worldboss" ? "/worldbosses" : "/instances"}/${i.slug}`, lastmod: instanceLastmod.get(i.id) ?? release });
      }
    }
    for (const b of bossRows) {
      if (b.slug) {
        urls.push({ path: `/bosses/${b.slug}`, lastmod: b.lastFight ?? release });
      }
    }
  }

  // Player profiles, single legion pages and per-difficulty boss variants stay reachable and indexable
  // but are not listed: thousands of thin, similar pages in the sitemap dilute the strong ones.

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
