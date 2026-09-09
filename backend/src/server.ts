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
import { playerRoutes } from "./routes/players.js";
import { serverRoutes } from "./routes/servers.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export async function buildServer() {
  const app = Fastify({ logger: true });

  await app.register(cors, { origin: env.CORS_ORIGIN });
  await app.register(rateLimit, {
    max: 30,
    timeWindow: "1 minute",
    // Only the upload endpoint needs protecting - the read-only leaderboard
    // routes are cheap, indexed lookups with no reason to throttle browsing.
    allowList: (request) => !request.url.startsWith("/api/uploads"),
  });

  await app.register(uploadRoutes);
  await app.register(instanceRoutes);
  await app.register(bossRoutes);
  await app.register(playerRoutes);
  await app.register(serverRoutes);

  await app.register(staticPlugin, {
    root: path.join(__dirname, "..", "public"),
  });

  return app;
}
