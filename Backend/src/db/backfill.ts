import { eq, isNull } from "drizzle-orm";
import { db } from "./client.js";
import { bosses, instances, serverCatalog } from "./schema.js";
import { GAME_NAME_TRANSLATIONS } from "../../../Web-Frontend/game-data.js";
import { slugify, uniqueSlug } from "../seo/slug.js";
import { UNASSIGNED_INSTANCE_NAME } from "../constants.js";

/** English display name for a stored (possibly German) instance/boss name, if the shared table knows one. */
export function englishNameFor(name: string): string | null {
  if (name === UNASSIGNED_INSTANCE_NAME) {
    return "Unassigned";
  }
  return GAME_NAME_TRANSLATIONS[name]?.en ?? null;
}

/**
 * Fills slug/name_en on rows that don't have them yet - runs after every migration (see
 * migrate.ts) so it covers both the one-off conversion of existing rows and any row a future
 * hand-written content migration inserts without a slug. Idempotent: rows with a slug are never
 * touched, so a slug stays stable even if the row is renamed later (URLs must not rot).
 */
export function backfillSlugs(): { serverCatalog: number; instances: number; bosses: number } {
  const counts = { serverCatalog: 0, instances: 0, bosses: 0 };

  const catalogTaken = new Set(
    db.select({ slug: serverCatalog.slug }).from(serverCatalog).all().map((r) => r.slug).filter((s): s is string => s !== null),
  );
  for (const row of db.select().from(serverCatalog).where(isNull(serverCatalog.slug)).all()) {
    const slug = uniqueSlug(slugify(row.name) || `server-${row.id}`, (s) => catalogTaken.has(s));
    catalogTaken.add(slug);
    db.update(serverCatalog).set({ slug }).where(eq(serverCatalog.id, row.id)).run();
    counts.serverCatalog++;
  }

  const allInstances = db.select().from(instances).all();
  const instanceTaken = new Map<string, Set<string>>();
  for (const row of allInstances) {
    if (row.slug !== null) {
      takenSet(instanceTaken, row.game).add(row.slug);
    }
  }
  for (const row of allInstances) {
    if (row.slug !== null) {
      continue;
    }
    const nameEn = row.nameEn ?? englishNameFor(row.name);
    const taken = takenSet(instanceTaken, row.game);
    const slug = uniqueSlug(slugify(nameEn ?? row.name) || `instance-${row.id}`, (s) => taken.has(s));
    taken.add(slug);
    db.update(instances).set({ slug, nameEn }).where(eq(instances.id, row.id)).run();
    counts.instances++;
  }

  const instanceById = new Map(db.select().from(instances).all().map((i) => [i.id, i]));
  const allBosses = db.select().from(bosses).all();
  const bossTaken = new Map<string, Set<string>>();
  for (const row of allBosses) {
    if (row.slug !== null) {
      takenSet(bossTaken, instanceById.get(row.instanceId)?.game ?? "aion").add(row.slug);
    }
  }
  for (const row of allBosses) {
    if (row.slug !== null) {
      continue;
    }
    const instance = instanceById.get(row.instanceId);
    const game = instance?.game ?? "aion";
    const nameEn = row.nameEn ?? englishNameFor(row.name);
    const taken = takenSet(bossTaken, game);
    const base = slugify(nameEn ?? row.name) || `boss-${row.id}`;
    // Same boss name in two dungeons of one game: qualify by dungeon before falling back to a
    // counter, so the URL still says which fight it is.
    const qualified = taken.has(base) && instance?.slug ? `${base}-${instance.slug}` : base;
    const slug = uniqueSlug(qualified, (s) => taken.has(s));
    taken.add(slug);
    db.update(bosses).set({ slug, nameEn }).where(eq(bosses.id, row.id)).run();
    counts.bosses++;
  }

  return counts;
}

function takenSet(map: Map<string, Set<string>>, game: string): Set<string> {
  let set = map.get(game);
  if (!set) {
    set = new Set();
    map.set(game, set);
  }
  return set;
}
