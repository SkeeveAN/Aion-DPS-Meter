import { mkdirSync } from "node:fs";
import { dirname } from "node:path";
import Database from "better-sqlite3";
import { drizzle } from "drizzle-orm/better-sqlite3";
import * as schema from "./schema.js";
import { env } from "../env.js";

// better-sqlite3 does not create the containing directory itself, and git
// does not track an empty one - a fresh checkout has no data/ at all.
mkdirSync(dirname(env.DATABASE_PATH), { recursive: true });

// Exported so tests can close the handle before the process exits: better-sqlite3 statements that
// are still alive at environment teardown trip a native assertion on recent Node versions.
export const sqlite = new Database(env.DATABASE_PATH);
sqlite.pragma("journal_mode = WAL");
// The production server has about 200 disk operations per second for everything on it, so the database is set up to ask the disk as little as possible:
// - synchronous NORMAL: in WAL mode the log is synced at checkpoints, not at every commit (an NC answer, an upload, ... each used to wait for an fsync); a
//   power loss can cost the last moments but never corrupts the file.
// - cache_size 256 MB (negative = KiB): pages stay in the process instead of in the file cache of the system, which a memory shortage empties (2.11.2026:
//   after that the cold start took a quarter of an hour at 0.5 MB/s). Only what is used is held.
// - a bigger checkpoint interval (4000 pages = 16 MB) so the log is written back in fewer, larger runs; the log file is kept at 64 MB at most.
sqlite.pragma("synchronous = NORMAL");
sqlite.pragma("cache_size = -262144");
sqlite.pragma("wal_autocheckpoint = 4000");
sqlite.pragma("journal_size_limit = 67108864");

export const db = drizzle(sqlite, { schema });
