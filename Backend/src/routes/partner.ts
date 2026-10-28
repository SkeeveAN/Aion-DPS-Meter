import type { FastifyInstance, FastifyReply, FastifyRequest } from "fastify";
import { announcementList } from "../announcements.js";
import { patchnoteList } from "../patchnotes.js";
import { twitchDropsView } from "../twitchDrops.js";
import { publicStats } from "../stats/collect.js";
import { cached } from "../seo/cache.js";
import * as api from "../partnerApi.js";
import { KEY_HEADER, findKey, rateCheck, touchKey } from "../partnerKeys.js";

const RESOURCES = ["classes", "instances", "bosses", "worldbosses", "maps", "events", "pets", "pets/:id", "titles", "wings", "stats", "announcements", "patchnotes", "twitch-drops"];

/**
 * The partner API: static game data (and the site's statistics, announcements, patch notes and Twitch drops) for other community sites, read-only,
 * behind a key in the X-API-Key header (keys are made with scripts/partner-key.ts). Every list takes ?lang=en|de|fr|es|ru|pt|ja|ko|zh, English by default.
 * The site's own routes under /api stay open; this part is the one a partner is meant to call from its server.
 */
export async function partnerRoutes(app: FastifyInstance) {
  await app.register(async (scope) => {
    scope.addHook("onRequest", async (request: FastifyRequest, reply: FastifyReply) => {
      const given = request.headers[KEY_HEADER];
      if (typeof given !== "string" || given === "") {
        return reply.status(401).send({ error: "api_key_required", message: `Send your key in the ${KEY_HEADER} header.` });
      }
      const key = findKey(given);
      if (!key) {
        return reply.status(401).send({ error: "invalid_api_key" });
      }
      const wait = rateCheck(key);
      reply.header("X-RateLimit-Limit", String(key.ratePerMinute));
      if (wait > 0) {
        return reply.status(429).header("Retry-After", String(wait)).send({ error: "rate_limited", retryAfterSeconds: wait });
      }
      touchKey(key.id);
      reply.header("Cache-Control", "private, max-age=300");
    });

    /** Answers a list in the language asked for; an unknown language is an error that names the ones there are. */
    function listRoute<T>(path: string, build: (lang: api.Lang, query: Record<string, string | undefined>) => T) {
      scope.get<{ Querystring: Record<string, string | undefined> }>(`/api/partner/v1/${path}`, async (request, reply) => {
        const lang = api.parseLang(request.query.lang);
        if (lang === null) {
          return reply.status(400).send({ error: "unsupported_lang", languages: api.LANGUAGES });
        }
        const items = build(lang, request.query);
        return { lang, ...(Array.isArray(items) ? { count: items.length, items } : (items as object)) };
      });
    }

    scope.get("/api/partner/v1", async () => ({ version: 1, defaultLang: api.DEFAULT_LANG, languages: api.LANGUAGES, resources: RESOURCES.map((r) => `/api/partner/v1/${r}`) }));

    listRoute("classes", () => api.classes());
    listRoute("instances", (lang) => api.instances(lang));
    listRoute("bosses", (lang, q) => api.bosses(lang, q.instance));
    listRoute("worldbosses", (lang) => api.worldBosses(lang));
    listRoute("maps", (lang) => api.maps(lang));
    listRoute("events", (lang) => api.events(lang));
    listRoute("pets", (lang, q) => api.pets(lang, q.category));
    listRoute("titles", (lang) => api.titles(lang));
    listRoute("wings", (lang) => api.wings(lang));

    scope.get<{ Params: { id: string }; Querystring: { lang?: string } }>("/api/partner/v1/pets/:id", async (request, reply) => {
      const lang = api.parseLang(request.query.lang);
      if (lang === null) {
        return reply.status(400).send({ error: "unsupported_lang", languages: api.LANGUAGES });
      }
      const found = /^\d+$/.test(request.params.id) ? api.pet(Number(request.params.id), lang) : null;
      return found ? { lang, ...found } : reply.status(404).send({ error: "not_found" });
    });

    // The site's statistics page (totals only), what NC announced and patched, and the Twitch campaigns.
    scope.get("/api/partner/v1/stats", async (_request, reply) => {
      reply.type("application/json").send(cached("stats:public", 300_000, () => JSON.stringify(publicStats())));
    });
    // NC publishes in en, de, fr, es, pt and ja; for another language the answer is English and says so in "lang".
    scope.get<{ Querystring: { lang?: string } }>("/api/partner/v1/announcements", async (request, reply) => {
      return api.parseLang(request.query.lang) === null ? reply.status(400).send({ error: "unsupported_lang", languages: api.LANGUAGES }) : announcementList(request.query.lang || api.DEFAULT_LANG);
    });
    scope.get<{ Querystring: { lang?: string } }>("/api/partner/v1/patchnotes", async (request, reply) => {
      return api.parseLang(request.query.lang) === null ? reply.status(400).send({ error: "unsupported_lang", languages: api.LANGUAGES }) : patchnoteList(request.query.lang || api.DEFAULT_LANG);
    });
    scope.get("/api/partner/v1/twitch-drops", async () => twitchDropsView());
  });
}
