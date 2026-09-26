import type { FastifyInstance, FastifyReply, FastifyRequest } from "fastify";
import { renderShell } from "../seo/shell.js";
import { renderHead } from "../seo/meta.js";
import { cached } from "../seo/cache.js";
import { isGame, type Game } from "../constants.js";
import {
  appOnlyPage,
  bossPage,
  downloadPage,
  encounterPage,
  homePage,
  instancePage,
  instancesPage,
  notFoundPage,
  playerPage,
  privacyPage,
  termsPage,
  type Page,
} from "../seo/pages.js";
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

type GameParams = { Params: { game: string } };

export async function pageRoutes(app: FastifyInstance) {
  app.get("/", async (request, reply) => send(reply, render(homePage(), requestPath(request))));
  app.get("/download", async (request, reply) => send(reply, render(downloadPage(), requestPath(request))));
  app.get("/privacy", async (request, reply) => send(reply, render(privacyPage(), requestPath(request))));
  app.get("/terms", async (request, reply) => send(reply, render(termsPage(), requestPath(request))));

  // Every game-scoped page validates the game segment first; an unknown one is a 404, not aion.
  const withGame =
    (handler: (game: Game, request: FastifyRequest, reply: FastifyReply) => FastifyReply | Promise<FastifyReply>) =>
    async (request: FastifyRequest<GameParams>, reply: FastifyReply) => {
      const { game } = request.params;
      if (!isGame(game)) {
        return sendNotFound(request, reply);
      }
      return handler(game, request, reply);
    };

  app.get<GameParams>("/:game(^(?:aion|aion2)$)", withGame(async (game, _request, reply) => reply.redirect(`/${game}/instances`, 301)));

  app.get<GameParams>(
    "/:game(^(?:aion|aion2)$)/instances",
    withGame((game, request, reply) => sendCached(request, reply, () => instancesPage(game))),
  );

  app.get<GameParams & { Params: { idOrSlug: string } }>(
    "/:game(^(?:aion|aion2)$)/instances/:idOrSlug",
    withGame((game, request, reply) => {
      const { idOrSlug } = request.params as { idOrSlug: string };
      // Old numeric links (shared before slugs existed) move to the slug URL for good.
      if (/^\d+$/.test(idOrSlug)) {
        const instance = findInstance(idOrSlug, game);
        return instance?.slug ? reply.redirect(`/${game}/instances/${instance.slug}`, 301) : sendNotFound(request, reply);
      }
      return sendCached(request, reply, () => instancePage(game, idOrSlug));
    }),
  );

  app.get<GameParams & { Params: { idOrSlug: string }; Querystring: { server?: string } }>(
    "/:game(^(?:aion|aion2)$)/bosses/:idOrSlug",
    withGame((game, request, reply) => {
      const { idOrSlug } = request.params as { idOrSlug: string };
      const query = request.query as { server?: string };
      if (/^\d+$/.test(idOrSlug)) {
        const found = findBoss(idOrSlug, game);
        if (!found?.boss.slug) {
          return sendNotFound(request, reply);
        }
        const suffix = query.server ? `?server=${encodeURIComponent(query.server)}` : "";
        return reply.redirect(`/${found.game}/bosses/${found.boss.slug}${suffix}`, 301);
      }
      return sendCached(request, reply, () => bossPage(game, idOrSlug, query));
    }),
  );

  app.get<GameParams & { Params: { id: string } }>(
    "/:game(^(?:aion|aion2)$)/players/:id",
    withGame((game, request, reply) => {
      const page = playerPage(game, (request.params as { id: string }).id);
      return page ? send(reply, render(page, requestPath(request))) : sendNotFound(request, reply);
    }),
  );

  const appOnly = (kind: "servers" | "search" | "participant") =>
    withGame((game, request, reply) => send(reply, render(appOnlyPage(game, kind, request.url.split("?")[0]), requestPath(request))));
  app.get<GameParams>("/:game(^(?:aion|aion2)$)/servers", appOnly("servers"));
  app.get<GameParams>("/:game(^(?:aion|aion2)$)/search", appOnly("search"));
  // Real boss name + server in the link preview, per the user - a shared encounter link used to
  // show only the generic "Boss fight details" placeholder regardless of which fight it was.
  app.get<GameParams & { Params: { id: string } }>(
    "/:game(^(?:aion|aion2)$)/encounters/:id",
    withGame((game, request, reply) => {
      const page = encounterPage(game, (request.params as { id: string }).id);
      return page ? send(reply, render(page, requestPath(request))) : sendNotFound(request, reply);
    }),
  );
  app.get<GameParams & { Params: { id: string } }>("/:game(^(?:aion|aion2)$)/participants/:id", appOnly("participant"));
}
