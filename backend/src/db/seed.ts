import { and, eq } from "drizzle-orm";
import { db } from "./client.js";
import { instances } from "./schema.js";
import { GAMES, UNASSIGNED_INSTANCE_NAME } from "../constants.js";

// The exact instance/boss roster of this particular private server isn't
// known here (see backend/README.md) - seeding a guessed list would risk
// silently misclassifying real uploads. What IS safe to seed is the bucket
// every unrecognized boss name falls into on first upload (see
// src/matching/merge.ts): a stable, well-known instance row for it avoids a
// race where two concurrent uploads for the same new boss each try to create
// their own "unassigned" instance. One bucket per game - an unknown Aion 2 boss
// must never land next to unknown Aion bosses.
function seedUnassignedInstances() {
  for (const game of GAMES) {
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
