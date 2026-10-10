import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.CLIENT_UPDATE_ENFORCE = "on";
process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-policy-")), "test.sqlite");
const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });

const { judgeClient, parseVersion, compareVersions, loadReleases, OUTDATED_MESSAGE } = await import("./clientPolicy.js");
const { uploadRoutes } = await import("./routes/uploads.js");
const { default: Fastify } = await import("fastify");

const HOUR = 3_600_000;
// release order by TIME: 0.9.57 came after 0.15.0, the numbers are no order
const releases = [
  { version: "0.15.0", at: 1_000 * HOUR },
  { version: "0.9.57", at: 1_100 * HOUR },
  { version: "0.28.0", at: 1_200 * HOUR },
  { version: "0.29.0", at: 1_300 * HOUR },
];
const NOW = 1_310 * HOUR;

test("versions are read strictly", () => {
  assert.deepEqual(parseVersion("0.29.0"), [0, 29, 0]);
  assert.deepEqual(parseVersion("v0.29.0+abc123"), [0, 29, 0]);
  assert.equal(parseVersion("test"), null);
  assert.equal(parseVersion(undefined), null);
  assert.equal(parseVersion("0.29"), null);
  assert.ok(compareVersions([0, 29, 0], [0, 9, 57]) > 0, "numerically 0.29.0 is above 0.9.57");
});

test("only the newest release may upload when there is no grace", () => {
  assert.equal(judgeClient("0.29.0", releases, NOW).allowed, true);
  const old = judgeClient("0.28.0", releases, NOW);
  assert.equal(old.allowed, false);
  assert.equal((old as { requiredVersion: string }).requiredVersion, "0.29.0");
});

test("a grace period keeps the previous release alive for that long after its successor", () => {
  assert.equal(judgeClient("0.28.0", releases, 1_300 * HOUR + 5 * HOUR, 6 * HOUR).allowed, true);
  assert.equal(judgeClient("0.28.0", releases, 1_300 * HOUR + 7 * HOUR, 6 * HOUR).allowed, false);
  assert.equal(judgeClient("0.15.0", releases, 1_300 * HOUR + 1 * HOUR, 6 * HOUR).allowed, false, "its successor is long out");
});

test("a version the server does not know: newer than the newest release is allowed (the server has not heard of it yet), anything else is not", () => {
  assert.equal(judgeClient("0.30.0", releases, NOW).allowed, true);
  assert.equal(judgeClient("0.9.58", releases, NOW).allowed, false, "a fork's number below the newest release");
  assert.equal(judgeClient("dev", releases, NOW).allowed, false);
  assert.equal(judgeClient(undefined, releases, NOW).allowed, false);
});

test("without a release list nobody is locked out", () => {
  assert.equal(judgeClient("0.1.0", [], NOW).allowed, true);
});

// --- the routes ---
const app = Fastify();
await app.register(uploadRoutes);
await app.ready();
const latest = loadReleases().at(-1)!.version;

test("an outdated client gets 426 with the message first, on both upload routes, before the payload is judged", async () => {
  for (const url of ["/api/uploads", "/api/uploads/profiles"]) {
    const res = await app.inject({ method: "POST", url, payload: { clientVersion: "0.9.57", garbage: true } });
    assert.equal(res.statusCode, 426, url);
    assert.ok(res.body.startsWith(`{"message":"${OUTDATED_MESSAGE.slice(0, 30)}`), "the message leads the body");
    const body = res.json() as { error: string; requiredVersion: string };
    assert.equal(body.error, "client_outdated");
    assert.equal(body.requiredVersion, latest);
  }
});

test("a client without a version is refused as well", async () => {
  const res = await app.inject({ method: "POST", url: "/api/uploads", payload: {} });
  assert.equal(res.statusCode, 426);
});

test("the current client passes the check (its payload is judged next)", async () => {
  const res = await app.inject({ method: "POST", url: "/api/uploads", payload: { clientVersion: latest } });
  assert.equal(res.statusCode, 400, "invalid payload, not 426");
});

test("the policy endpoint tells the client what to be", async () => {
  const res = await app.inject({ method: "GET", url: "/api/client-policy" });
  assert.equal(res.statusCode, 200);
  const body = res.json() as { requiredVersion: string; enforce: boolean };
  assert.equal(body.requiredVersion, latest);
  assert.equal(body.enforce, true);
});

test("the policy endpoint judges the version the client names", async () => {
  const outdated = (await app.inject({ method: "GET", url: "/api/client-policy?version=0.9.57" })).json() as { allowed: boolean };
  assert.equal(outdated.allowed, false);
  const current = (await app.inject({ method: "GET", url: `/api/client-policy?version=${latest}` })).json() as { allowed: boolean };
  assert.equal(current.allowed, true);
});

test("the rule can be switched off", async () => {
  process.env.CLIENT_UPDATE_ENFORCE = "off";
  try {
    const res = await app.inject({ method: "POST", url: "/api/uploads", payload: { clientVersion: "0.9.57" } });
    assert.equal(res.statusCode, 400, "not 426");
  } finally {
    process.env.CLIENT_UPDATE_ENFORCE = "on";
  }
  await app.close();
  sqlite.close();
});
