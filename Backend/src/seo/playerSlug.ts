import { eq } from "drizzle-orm";
import { db } from "../db/client.js";
import { players, servers } from "../db/schema.js";
import { slugify, uniqueSlug } from "./slug.js";

/** "Europe - Kaisinel" -> "kaisinel": the region adds nothing to a player link. */
function serverPart(displayName: string | null): string {
  return slugify((displayName ?? "").split(" - ").pop() ?? "");
}

/**
 * Gives a player a slug if it has none: "<name>-<server>" ("aahz-kaisinel"), a counter on the
 * rare clash, "player-<id>" for a name without any Latin letters. An existing slug is never
 * changed - a rename must not break links that were already shared.
 */
export function ensurePlayerSlug(playerId: number): string | null {
  const row = db
    .select({ id: players.id, name: players.name, slug: players.slug, serverName: servers.displayName })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .where(eq(players.id, playerId))
    .get();
  if (!row) {
    return null;
  }
  if (row.slug) {
    return row.slug;
  }
  const name = slugify(row.name);
  const server = serverPart(row.serverName);
  const base = name ? [name, server].filter(Boolean).join("-") : `player-${row.id}`;
  const slug = uniqueSlug(base, (s) => db.select({ id: players.id }).from(players).where(eq(players.slug, s)).get() !== undefined);
  db.update(players).set({ slug }).where(eq(players.id, row.id)).run();
  return slug;
}
