import type { FastifyInstance, FastifyReply, FastifyRequest } from "fastify";
import { renderShell } from "../seo/shell.js";
import { renderHead } from "../seo/meta.js";
import { cached } from "../seo/cache.js";
import { env } from "../env.js";
import { DEFAULT_GAME, type Game } from "../constants.js";
import {
  appOnlyPage,
  bossPage,
  instanceCategoryPage,
  downloadPage,
  changelogPage,
  featuresPage,
  statsPage,
  guildsPage,
  guildPage,
  privateStatsPage,
  feedbackPage,
  comparePage,
  encounterPage,
  participantPage,
  homePage,
  instancePage,
  instancesPage,
  worldBossesPage,
  notFoundPage,
  playerPage,
  privacyPage,
  termsPage,
  type Page,
} from "../seo/pages.js";
import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { players } from "../db/schema.js";
import { ensurePlayerSlug } from "../seo/playerSlug.js";
import { findBoss } from "./bosses.js";
import { findInstance } from "./instances.js";

const CONTENT_TTL_MS = 120_000;

interface Rendered {
  status: number;
  html: string;
}

/** The address as the browser sees it - what app.js compares against to decide whether to hydrate. */
function requestPath(request: FastifyRequest): string {
  return request.raw.url ?? request.url;
}

function render(page: Page, ssrPath: string): Rendered {
  const app = `<div data-ssr="${ssrPath.replace(/"/g, "&quot;")}">${page.body.value}</div>`;
  return { status: page.status, html: renderShell({ head: renderHead(page.meta), app }) };
}

function send(reply: FastifyReply, rendered: Rendered): FastifyReply {
  return reply.status(rendered.status).type("text/html; charset=utf-8").header("Cache-Control", "no-cache").send(rendered.html);
}

/** Renders through the page cache; a null page (unknown slug) is cached as a 404 too, so a bot hammering a dead URL stays cheap. */
function sendCached(request: FastifyRequest, reply: FastifyReply, build: () => Page | null): FastifyReply {
  const path = requestPath(request);
  const rendered = JSON.parse(
    cached(`page:${path}`, CONTENT_TTL_MS, () => JSON.stringify(render(build() ?? notFoundPage(request.url.split("?")[0]), path))),
  ) as Rendered;
  return send(reply, rendered);
}

export function sendNotFound(request: FastifyRequest, reply: FastifyReply): FastifyReply {
  return send(reply, render(notFoundPage(request.url.split("?")[0]), requestPath(request)));
}

type GameParams = Record<string, never>;

export async function pageRoutes(app: FastifyInstance) {
  app.get("/", async (request, reply) => send(reply, render(homePage(), requestPath(request))));
  app.get("/download", async (request, reply) => send(reply, render(downloadPage(), requestPath(request))));
  app.get("/features", async (request, reply) => send(reply, render(featuresPage(), requestPath(request))));
  app.get("/changelog", async (request, reply) => send(reply, render(changelogPage(), requestPath(request))));
  app.get("/legions", async (request, reply) => send(reply, render(guildsPage(), requestPath(request))));
  app.get<{ Params: { slug: string } }>("/legions/:slug", async (request, reply) => sendCached(request, reply, () => guildPage(request.params.slug)));
  // The pages were first published as /guilds; those links keep working.
  app.get("/guilds", async (_request, reply) => reply.redirect("/legions", 301));
  app.get<{ Params: { slug: string } }>("/guilds/:slug", async (request, reply) => reply.redirect(`/legions/${encodeURIComponent(request.params.slug)}`, 301));
  app.get("/stats", async (request, reply) => send(reply, render(statsPage(), requestPath(request))));
  // The secret address of the operator statistics: anything else under /p/ is an ordinary 404.
  app.get<{ Params: { secret: string } }>("/p/:secret", async (request, reply) => {
    if (!env.STATS_SECRET || request.params.secret !== env.STATS_SECRET) {
      return sendNotFound(request, reply);
    }
    return send(reply, render(privateStatsPage(requestPath(request)), requestPath(request))).header("X-Robots-Tag", "noindex, nofollow").header("Cache-Control", "no-store");
  });
  app.get("/feedback", async (request, reply) => send(reply, render(feedbackPage(), requestPath(request))));
  app.get("/privacy", async (request, reply) => send(reply, render(privacyPage(), requestPath(request))));
  app.get("/terms", async (request, reply) => send(reply, render(termsPage(), requestPath(request))));

  // Every game-scoped page validates the game segment first; an unknown one is a 404, not aion.
  // Only Aion 2 is served, so the game is a constant now rather than part of the address.
  const withGame =
    (handler: (game: Game, request: FastifyRequest, reply: FastifyReply) => FastifyReply | Promise<FastifyReply>) =>
    async (request: FastifyRequest, reply: FastifyReply) =>
      handler(DEFAULT_GAME, request, reply);

  // The game segment used to be part of every address (/aion2/bosses/...). Only Aion 2 is served, so
  // the old links move to the same path without it for good.
  app.get<{ Params: { "*": string } }>("/aion2", async (request, reply) => reply.redirect("/instances", 301));
  app.get<{ Params: { "*": string } }>("/aion2/*", async (request, reply) => {
    const query = request.url.includes("?") ? request.url.slice(request.url.indexOf("?")) : "";
    return reply.redirect(`/${request.params["*"]}${query}`, 301);
  });

  app.get<GameParams>(
    "/instances",
    withGame((game, request, reply) => sendCached(request, reply, () => instancesPage(game))),
  );

  app.get<GameParams>(
    "/worldbosses",
    withGame((game, request, reply) => sendCached(request, reply, () => worldBossesPage(game))),
  );

  app.get<GameParams & { Params: { slug: string } }>(
    "/worldbosses/:slug",
    withGame((game, request, reply) => {
      const slug = (request.params as { slug: string }).slug;
      const found = findInstance(slug, game);
      if (!found || found.category !== "worldboss") {
        return sendNotFound(request, reply);
      }
      return sendCached(request, reply, () => instancePage(game, slug));
    }),
  );

  app.get<GameParams & { Params: { idOrSlug: string } }>(
    "/instances/:idOrSlug",
    withGame((game, request, reply) => {
      const { idOrSlug } = request.params as { idOrSlug: string };
      // World bosses live under /worldbosses now (their old category address and the areas move there for good).
      if (idOrSlug === "worldboss") {
        return reply.redirect("/worldbosses", 301);
      }
      const asArea = /^\d+$/.test(idOrSlug) ? null : findInstance(idOrSlug, game);
      if (asArea?.category === "worldboss" && asArea.slug) {
        return reply.redirect(`/worldbosses/${asArea.slug}`, 301);
      }
      // A category of the overview has its own address (/instances/nightmare ...).
      const categoryPage = instanceCategoryPage(game, idOrSlug, null);
      if (categoryPage) {
        return send(reply, render(categoryPage, requestPath(request)));
      }
      // Old numeric links (shared before slugs existed) move to the slug URL for good.
      if (/^\d+$/.test(idOrSlug)) {
        const instance = findInstance(idOrSlug, game);
        return instance?.slug ? reply.redirect(`/instances/${instance.slug}`, 301) : sendNotFound(request, reply);
      }
      return sendCached(request, reply, () => instancePage(game, idOrSlug));
    }),
  );

  app.get<GameParams & { Params: { variant: string } }>(
    "/instances/expedition/:variant",
    withGame((game, request, reply) => {
      const variant = (request.params as { variant: string }).variant;
      const page = variant === "hard" || variant === "normal" ? instanceCategoryPage(game, "expedition", variant) : null;
      if (variant === "normal") {
        return reply.redirect("/instances/expedition", 301);
      }
      return page ? send(reply, render(page, requestPath(request))) : sendNotFound(request, reply);
    }),
  );

  app.get<GameParams & { Params: { idOrSlug: string }; Querystring: { server?: string; mode?: string } }>(
    "/bosses/:idOrSlug",
    withGame((game, request, reply) => {
      const { idOrSlug } = request.params as { idOrSlug: string };
      const query = request.query as { server?: string; mode?: string };
      if (/^\d+$/.test(idOrSlug)) {
        const found = findBoss(idOrSlug, game);
        if (!found?.boss.slug) {
          return sendNotFound(request, reply);
        }
        const suffix = query.server ? `?server=${encodeURIComponent(query.server)}` : query.mode ? `?mode=${encodeURIComponent(query.mode)}` : "";
        return reply.redirect(`/bosses/${found.boss.slug}${suffix}`, 301);
      }
      return sendCached(request, reply, () => bossPage(game, idOrSlug, query));
    }),
  );

  app.get<GameParams & { Params: { id: string } }>(
    "/players/:id",
    withGame((game, request, reply) => {
      const id = (request.params as { id: string }).id;
      // Old numeric links move to the name slug for good.
      if (/^\d+$/.test(id)) {
        const row = db.select({ id: players.id }).from(players).where(eq(players.id, Number(id))).get();
        const slug = row ? ensurePlayerSlug(row.id) : null;
        return slug ? reply.redirect(`/players/${slug}`, 301) : sendNotFound(request, reply);
      }
      const page = playerPage(game, id);
      return page ? send(reply, render(page, requestPath(request))) : sendNotFound(request, reply);
    }),
  );

  const appOnly = (kind: "servers" | "search" | "participant" | "compare") =>
    withGame((game, request, reply) => send(reply, render(appOnlyPage(game, kind, request.url.split("?")[0]), requestPath(request))));
  app.get<GameParams>("/search", appOnly("search"));
  app.get<GameParams>("/compare/runs", withGame((_game, request, reply) => send(reply, render(comparePage("runs", request.query as { a?: string; b?: string }, request.url.split("?")[0]), requestPath(request)))));
  app.get<GameParams>("/compare/players", withGame((_game, request, reply) => send(reply, render(comparePage("players", request.query as { a?: string; b?: string; boss?: string }, request.url.split("?")[0]), requestPath(request)))));
  // Real boss name + server in the link preview, per the user - a shared encounter link used to
  // show only the generic "Boss fight details" placeholder regardless of which fight it was.
  app.get<GameParams & { Params: { id: string } }>(
    "/encounters/:id",
    withGame((game, request, reply) => {
      const page = encounterPage(game, (request.params as { id: string }).id);
      return page ? send(reply, render(page, requestPath(request))) : sendNotFound(request, reply);
    }),
  );
  app.get<GameParams & { Params: { id: string } }>(
    "/participants/:id",
    withGame((game, request, reply) => {
      const page = participantPage(game, (request.params as { id: string }).id);
      return page ? send(reply, render(page, requestPath(request))) : sendNotFound(request, reply);
    }),
  );
}
