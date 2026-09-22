import path from "node:path";
import { fileURLToPath } from "node:url";
import Fastify from "fastify";
import cors from "@fastify/cors";
import rateLimit from "@fastify/rate-limit";
import staticPlugin from "@fastify/static";
import { env } from "./env.js";
import { uploadRoutes } from "./routes/uploads.js";
import { instanceRoutes } from "./routes/instances.js";
import { bossRoutes } from "./routes/bosses.js";
import { encounterRoutes } from "./routes/encounters.js";
import { playerRoutes } from "./routes/players.js";
import { serverRoutes } from "./routes/servers.js";
import { serverCatalogRoutes } from "./routes/serverCatalog.js";
import { seoRoutes } from "./routes/seo.js";
import { pageRoutes, sendNotFound } from "./routes/pages.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export async function buildServer() {
  const app = Fastify({ logger: true });

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
    allowList: (request) => !request.url.startsWith("/api/uploads"),
  });

  await app.register(uploadRoutes);
  await app.register(instanceRoutes);
  await app.register(bossRoutes);
  await app.register(encounterRoutes);
  await app.register(playerRoutes);
  await app.register(serverRoutes);
  await app.register(serverCatalogRoutes);
  await app.register(seoRoutes);
  await app.register(pageRoutes);

  // Assets only - HTML comes from pageRoutes above so it can carry per-page <head> content and
  // stay uncached, while everything here may sit in browser caches for an hour (revalidated via ETag).
  await app.register(staticPlugin, {
    root: path.join(__dirname, "..", "public"),
    index: false,
    maxAge: "1h",
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
