import { after, test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

process.env.DATABASE_PATH = path.join(mkdtempSync(path.join(tmpdir(), "dpsmeter-archive-test-")), "test.sqlite");

const { migrate } = await import("drizzle-orm/better-sqlite3/migrator");
const { db, sqlite } = await import("./db/client.js");
migrate(db, { migrationsFolder: path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "drizzle") });
after(() => sqlite.close());

const { compressSettledUploads, dropOldProfilePayloads, rawPayloadOf } = await import("./uploadArchive.js");
const { uploads } = await import("./db/schema.js");
const { eq } = await import("drizzle-orm");

function add(status: "merged" | "pending" | "rejected", receivedAt: string, raw: string): number {
  return db.insert(uploads).values({ uploaderReportedName: "T", ipHash: "h", status, receivedAt, rawPayloadJson: raw }).returning({ id: uploads.id }).get().id;
}
const old = "2020-01-01 00:00:00";
const row = (id: number) => db.select().from(uploads).where(eq(uploads.id, id)).get()!;

test("settled uploads older than a day are packed, everything else keeps its plain text", () => {
  const text = JSON.stringify({ bossNpcId: 123, filler: "x".repeat(5000) });
  const merged = add("merged", old, text);
  const pending = add("pending", old, text);
  const fresh = add("merged", new Date().toISOString().replace("T", " ").slice(0, 19), text);
  assert.equal(compressSettledUploads(24), 1);
  assert.equal(row(merged).rawPayloadJson, "");
  assert.ok(row(merged).rawPayloadGz && row(merged).rawPayloadGz!.length < text.length / 5);
  assert.equal(rawPayloadOf(row(merged)), text);
  assert.equal(rawPayloadOf(row(pending)), text);
  assert.equal(row(pending).rawPayloadGz, null);
  assert.equal(rawPayloadOf(row(fresh)), text);
  assert.equal(compressSettledUploads(24), 0); // nothing left to do
});

test("the payload of an old profile-only upload is dropped, that of a fight upload stays", () => {
  const profileOnly = add("merged", old, "{}");
  compressSettledUploads(24);
  assert.ok(rawPayloadOf(row(profileOnly)) !== null);
  assert.ok(dropOldProfilePayloads(90) >= 1);
  assert.equal(rawPayloadOf(row(profileOnly)), null);
  assert.equal(row(profileOnly).status, "merged"); // the row stays for the statistics
});
