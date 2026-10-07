import type { FastifyInstance } from "fastify";
import { db } from "../db/client.js";
import { downloads } from "../db/schema.js";
import { hashIp } from "../ipHash.js";

const REPO = "SkeeveAN/Aion-DPS-Meter";
const FALLBACK = `https://github.com/${REPO}/releases/latest`;
const CACHE_MS = 5 * 60_000;
const BOT = /bot|crawl|spider|slurp|preview|facebookexternalhit|discord|slack|curl|wget|python|go-http|headless/i;

let cache: { at: number; tag: string; url: string } | null = null;

/** The newest release's installer, from GitHub's list (releases[0] is the newest even when marked prerelease). */
async function latestInstaller(): Promise<{ tag: string; url: string } | null> {
  if (cache && Date.now() - cache.at < CACHE_MS) {
    return cache;
  }
  try {
    const res = await fetch(`https://api.github.com/repos/${REPO}/releases?per_page=5`, {
      headers: { accept: "application/vnd.github+json", "user-agent": "aiondps.com" },
      signal: AbortSignal.timeout(5000),
    });
    if (!res.ok) {
      return cache;
    }
    const releases = (await res.json()) as { tag_name: string; draft?: boolean; assets: { name: string; browser_download_url: string }[] }[];
    const release = releases.find((r) => !r.draft && r.assets.some((a) => a.name.endsWith("-Setup.exe")));
    const asset = release?.assets.find((a) => a.name.endsWith("-Setup.exe"));
    if (release && asset) {
      cache = { at: Date.now(), tag: release.tag_name, url: asset.browser_download_url };
    }
  } catch {
    // GitHub unreachable: keep serving the last known installer, or fall back to the releases page
  }
  return cache;
}

export async function downloadRoutes(app: FastifyInstance) {
  // The website's download button: counts the click, then sends the browser to GitHub's own file.
  app.get("/download/latest", async (request, reply) => {
    const latest = await latestInstaller();
    try {
      db.insert(downloads)
        .values({ ipHash: hashIp(request.ip), tag: latest?.tag ?? "unknown", isBot: BOT.test(String(request.headers["user-agent"] ?? "")) || !request.headers["user-agent"] })
        .run();
    } catch (err) {
      app.log.warn({ err }, "download click not stored");
    }
    return reply.header("Cache-Control", "no-store").redirect(latest?.url ?? FALLBACK, 302);
  });
}
