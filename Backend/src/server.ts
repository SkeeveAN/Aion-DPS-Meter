import path from "node:path";
import { fileURLToPath } from "node:url";
import Fastify from "fastify";
import cors from "@fastify/cors";
import rateLimit from "@fastify/rate-limit";
import staticPlugin from "@fastify/static";
import { env } from "./env.js";
import { uploadRoutes } from "./routes/uploads.js";
import { feedbackRoutes } from "./routes/feedback.js";
import { instanceRoutes } from "./routes/instances.js";
import { bossRoutes } from "./routes/bosses.js";
import { encounterRoutes } from "./routes/encounters.js";
import { playerRoutes } from "./routes/players.js";
import { compareRoutes } from "./routes/compare.js";
import { serverRoutes } from "./routes/servers.js";
import { serverCatalogRoutes } from "./routes/serverCatalog.js";
import { statsRoutes } from "./routes/stats.js";
import { siteStatsRoutes } from "./routes/siteStats.js";
import { serverStatusRoutes } from "./routes/serverStatus.js";
import { downloadRoutes } from "./routes/downloads.js";
import { guildRoutes } from "./routes/guilds.js";
import { seoRoutes } from "./routes/seo.js";
import { pageRoutes, sendNotFound } from "./routes/pages.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export async function buildServer() {
  const app = Fastify({
    logger: true,
    // Listens on 127.0.0.1 behind nginx only, which overwrites X-Forwarded-For with the real peer
    // address; without this every visitor shows up as 127.0.0.1 and shares one rate-limit bucket.
    trustProxy: true,
  });

  await app.register(cors, { origin: env.CORS_ORIGIN });
  await app.register(rateLimit, {
    // 30/min looked generous until a real client hit it: "Upload last run"/"Reload from
    // Chat.log" (see the client's MainWindow.xaml.cs) can legitimately fire dozens of uploads in
    // one burst - a full-file reload surfaces every distinct boss/mob across however many real
    // sessions the log spans, and the client sends them back-to-back with no delay. 120/min gives
    // real headroom for that while still bounding a single IP.
    max: 120,
    timeWindow: "1 minute",
    // Only the upload endpoint needs protecting - the read-only leaderboard
    // routes are cheap, indexed lookups with no reason to throttle browsing.
    allowList: (request) => !request.url.startsWith("/api/uploads") && !request.url.startsWith("/api/feedback"),
  });

  await app.register(uploadRoutes);
  await app.register(feedbackRoutes);
  await app.register(instanceRoutes);
  await app.register(bossRoutes);
  await app.register(encounterRoutes);
  await app.register(playerRoutes);
  await app.register(compareRoutes);
  await app.register(serverRoutes);
  await app.register(serverCatalogRoutes);
  await app.register(statsRoutes);
  await app.register(siteStatsRoutes);
  await app.register(serverStatusRoutes);
  await app.register(downloadRoutes);
  await app.register(guildRoutes);
  await app.register(seoRoutes);
  await app.register(pageRoutes);

  // Assets only - HTML comes from pageRoutes above so it can carry per-page <head> content and
  // stay uncached, while everything here may sit in browser caches for an hour (revalidated via ETag).
  await app.register(staticPlugin, {
    root: path.join(__dirname, "..", "..", "Web-Frontend"),
    index: false,
    maxAge: "1h",
  });

  // The site's own code and data (app.js, style.css, translations, changelog.json ...) must never be served
  // stale: after a deploy an old cached app.js would not know the new pages. "no-cache" still lets the browser
  // keep the file, it just asks first (ETag) and gets a tiny 304 when nothing changed.
  app.addHook("onSend", async (request, reply) => {
    if (reply.statusCode === 200 && /\.(js|css|json)$/.test(request.url.split("?")[0]) && !request.url.startsWith("/api/")) {
      reply.header("cache-control", "no-cache");
    }
  });

  // Pictures change rarely and are only replaced under a new name: a week in the browser cache keeps repeat
  // visits from fetching dozens of card photos and icons again.
  app.addHook("onSend", async (request, reply) => {
    if (reply.statusCode === 200 && (request.url.startsWith("/images/") || request.url.startsWith("/icons/"))) {
      reply.header("cache-control", "public, max-age=604800");
    }
  });

  // A wrong address must answer 404 with a real page, never 200 with an empty app shell - crawlers
  // would otherwise index every typo as a page. API callers keep getting JSON.
  app.setNotFoundHandler((request, reply) => {
    if (request.url.startsWith("/api/")) {
      return reply.status(404).send({ error: "not_found" });
    }
    return sendNotFound(request, reply);
  });

  return app;
}
