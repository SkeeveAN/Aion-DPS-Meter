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
  BASE_URL: (process.env.BASE_URL ?? "https://aiondps.com").replace(/\/+$/, ""),
};
