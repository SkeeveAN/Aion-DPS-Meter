import { writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import type { FastifyInstance } from "fastify";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const LAYOUT_FILE = path.join(__dirname, "..", "..", "..", "Web-Frontend", "feature-layout.json");

/**
 * Save endpoint of the features-page designer (Web-Frontend/feature-designer.js). Only exists when the
 * local .env sets FEATURE_DESIGNER=1 and then still only answers a direct loopback request (nginx adds
 * X-Forwarded-For, so nothing from outside gets through).
 */
export async function devFeatureLayoutRoutes(app: FastifyInstance) {
  if (process.env.FEATURE_DESIGNER !== "1") return;
  app.post("/api/dev/feature-layout", async (request, reply) => {
    const loopback = ["127.0.0.1", "::1", "::ffff:127.0.0.1"].includes(request.socket.remoteAddress ?? "");
    if (!loopback || request.headers["x-forwarded-for"]) return reply.status(403).send({ error: "forbidden" });
    const body = request.body as Record<string, Record<string, unknown>> | null;
    if (!body || typeof body !== "object" || Array.isArray(body)) return reply.status(400).send({ error: "bad_layout" });
    const clean: Record<string, Record<string, [number, number] | string>> = {};
    for (const [scene, items] of Object.entries(body)) {
      if (!/^[a-z]+$/.test(scene) || typeof items !== "object" || !items) continue;
      for (const [key, pos] of Object.entries(items)) {
        if (key === "bg") {
          if (typeof pos === "string" && /^[a-z0-9-]+$/.test(pos)) (clean[scene] ??= {}).bg = pos as never;
          continue;
        }
        if (!/^(copy|win\d)$/.test(key) || !Array.isArray(pos)) continue;
        const [x, y] = pos.map((n) => Math.round(Number(n)));
        if (!Number.isFinite(x) || !Number.isFinite(y)) continue;
        (clean[scene] ??= {})[key] = [x, y];
      }
    }
    await writeFile(LAYOUT_FILE, JSON.stringify(clean, null, 2) + "\n");
    return { ok: true };
  });
}
