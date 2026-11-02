import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-receipt-")), "test.sqlite");
process.env.CLIENT_UPDATE_ENFORCE = "off";

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
const { default: Fastify } = await import("fastify");
const { uploadRoutes } = await import("./routes/uploads.js");

const app = Fastify({ logger: false });
await app.register(uploadRoutes);
await app.ready();
after(async () => {
  await app.close();
  sqlite.close();
});

test("the receipt route refuses what is no receipt and says unknown for a fight it has not seen", async () => {
  const bad = await app.inject({ method: "GET", url: "/api/uploads/receipt/not-a-receipt" });
  assert.equal(bad.statusCode, 400);
  const unknown = await app.inject({ method: "GET", url: `/api/uploads/receipt/${"a".repeat(64)}` });
  assert.equal(unknown.statusCode, 200);
  assert.deepEqual(unknown.json(), { state: "unknown", encounterId: null });
});
