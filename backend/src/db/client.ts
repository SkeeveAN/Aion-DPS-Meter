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

export const db = drizzle(sqlite, { schema });
