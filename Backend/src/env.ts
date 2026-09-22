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
  BASE_URL: (process.env.BASE_URL ?? "https://aiondps.com").replace(/\/+$/, ""),
};
