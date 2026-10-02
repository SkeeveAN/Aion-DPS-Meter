import { and, eq } from "drizzle-orm";
import { db } from "./client.js";
import { instances } from "./schema.js";
import { DEFAULT_GAME, UNASSIGNED_INSTANCE_NAME } from "../constants.js";

// The instance/boss roster comes from src/data/aion2 (see content/syncAion2.ts). What is seeded
// here is the bucket every unrecognized boss name falls into on first upload (see
// src/matching/merge.ts): a stable, well-known instance row for it avoids a race where two
// concurrent uploads for the same new boss each try to create their own "unassigned" instance.
function seedUnassignedInstances() {
  for (const game of [DEFAULT_GAME]) {
    const existing = db
      .select()
      .from(instances)
      .where(and(eq(instances.name, UNASSIGNED_INSTANCE_NAME), eq(instances.game, game)))
      .get();

    if (existing) {
      console.log(`Unassigned-instance bucket for ${game} already seeded, skipping.`);
      continue;
    }

    db.insert(instances)
      .values({ name: UNASSIGNED_INSTANCE_NAME, nameEn: "Unassigned", slug: "unassigned", game, sortOrder: -1 })
      .run();
    console.log(`Seeded the unassigned-instance bucket for ${game}.`);
  }
}

seedUnassignedInstances();
