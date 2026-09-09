import "dotenv/config";

export const env = {
  PORT: Number(process.env.PORT ?? 4000),
  HOST: process.env.HOST ?? "127.0.0.1",
  DATABASE_PATH: process.env.DATABASE_PATH ?? "./data/dpsmeter.sqlite",
  CORS_ORIGIN: process.env.CORS_ORIGIN ?? "*",
};
