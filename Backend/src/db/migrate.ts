import { migrate } from "drizzle-orm/better-sqlite3/migrator";
import { db } from "./client.js";
import { backfillSlugs } from "./backfill.js";
import { seedBossHpReference } from "../bossHp.js";

migrate(db, { migrationsFolder: "./drizzle" });
console.log("Migrations applied.");

const filled = backfillSlugs();
console.log(`Slugs backfilled: ${filled.serverCatalog} servers, ${filled.instances} instances, ${filled.bosses} bosses, ${filled.players} players, ${filled.guilds} legions.`);

console.log(`Boss hit-point reference: ${seedBossHpReference()} rows added from the seed.`);
