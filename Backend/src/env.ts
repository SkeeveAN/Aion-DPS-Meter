import "dotenv/config";

export const env = {
  PORT: Number(process.env.PORT ?? 4000),
  HOST: process.env.HOST ?? "127.0.0.1",
  DATABASE_PATH: process.env.DATABASE_PATH ?? "./data/dpsmeter.sqlite",
  // The desktop client is not a browser and the web frontend is same-origin, so nothing needs a
  // wildcard here; only ever widen this for a deliberate third-party integration.
  CORS_ORIGIN: process.env.CORS_ORIGIN ?? "https://aiondps.com",
  // Public origin used for canonical URLs, sitemap and Open Graph tags - never derived from request
  // headers, so a misconfigured proxy can't make the site advertise the wrong host.
  // Feedback forwarding (routes/feedback.ts). The token is a fine-grained PAT with Issues:write on
  // the issues repo and Contents:write on the private data repo; without it the endpoint answers 503.
  GITHUB_TOKEN: process.env.GITHUB_TOKEN ?? "",
  GITHUB_ISSUES_REPO: process.env.GITHUB_ISSUES_REPO ?? "SkeeveAN/Aion-DPS-Meter",
  GITHUB_DATA_REPO: process.env.GITHUB_DATA_REPO ?? "",
  // Private statistics page lives at /p/<STATS_SECRET> and /metrics needs "Authorization: Bearer <METRICS_TOKEN>";
  // both stay switched off (404) while empty.
  STATS_SECRET: process.env.STATS_SECRET ?? "",
  METRICS_TOKEN: process.env.METRICS_TOKEN ?? "",
  // Login servers to probe on the status page, "host:port,host:port". Found 2026-10-09 by watching the game's own connections: the client talks to 193.202.112.93:13700 (the lobby: login, server list)
  // right before it connects to the world server on port 13328.
  LOGIN_SERVERS: process.env.LOGIN_SERVERS ?? "193.202.112.93:13700",
  // Twitch app (dev.twitch.tv/console, "Confidential"): only used for the public streams list of /twitch-drops; without both the live part stays empty.
  TWITCH_CLIENT_ID: process.env.TWITCH_CLIENT_ID ?? "",
  TWITCH_CLIENT_SECRET: process.env.TWITCH_CLIENT_SECRET ?? "",
  // What happens to a boss upload that looks implausible (uploadReview.ts): "off" nothing, "log" it is written to the log and merged as before (the dry run: look at the numbers first),
  // "on" it waits in the review queue and stays off the leaderboards until approved. ADMIN_TOKEN opens /api/admin/uploads/* (Authorization: Bearer ...); empty = switched off (404).
  UPLOAD_REVIEW: (["off", "log", "on"].includes(process.env.UPLOAD_REVIEW ?? "") ? process.env.UPLOAD_REVIEW : "log") as "off" | "log" | "on",
  ADMIN_TOKEN: process.env.ADMIN_TOKEN ?? "",
  BASE_URL: (process.env.BASE_URL ?? "https://aiondps.com").replace(/\/+$/, ""),
};
