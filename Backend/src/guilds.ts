import { createHash } from "node:crypto";
import { and, eq, isNotNull, like, ne } from "drizzle-orm";
import { db } from "./db/client.js";
import { playerProfiles, players, servers } from "./db/schema.js";
import { AION2_CLASS_BY_ID } from "./constants.js";
import { slugify, uniqueSlug } from "./seo/slug.js";

/**
 * Legions are not a table of their own: the game names a player's legion next to the character, and
 * players.guild keeps the latest one that was seen. A legion is therefore a name on one server, and its
 * members are the players we have seen under that name - not necessarily everybody who is in it.
 */
export interface GuildInfo {
  slug: string;
  name: string;
  serverId: number;
  serverName: string;
  memberCount: number;
}

export interface GuildMember {
  id: number;
  slug: string | null;
  name: string;
  className: string | null;
  level: number | null;
  faction: number | null;
}

/** Every legion with a stable slug "<legion>-<server>": a Latin name reads in the address, any other falls back to a short hash. */
export function listGuilds(): GuildInfo[] {
  const rows = db
    .select({ guild: players.guild, serverId: players.serverId, serverName: servers.displayName })
    .from(players)
    .innerJoin(servers, eq(players.serverId, servers.id))
    .where(and(isNotNull(players.guild), ne(players.guild, ""), like(servers.fingerprint, "aion2:%")))
    .all();

  const counts = new Map<string, { name: string; serverId: number; serverName: string; count: number }>();
  for (const r of rows) {
    const guild = (r.guild ?? "").trim();
    if (!guild || r.serverId === null) {
      continue;
    }
    const key = `${r.serverId}\u0000${guild}`;
    const entry = counts.get(key) ?? { name: guild, serverId: r.serverId, serverName: r.serverName ?? "", count: 0 };
    entry.count += 1;
    counts.set(key, entry);
  }

  // Sorted first, so a clash of two names that slugify alike always resolves the same way.
  const sorted = [...counts.values()].sort((a, b) => a.serverId - b.serverId || a.name.localeCompare(b.name));
  const taken = new Set<string>();
  return sorted.map((g) => {
    const server = slugify(g.serverName) || `server-${g.serverId}`;
    const base = slugify(g.name)
      ? `${slugify(g.name)}-${server}`
      : `legion-${createHash("sha1").update(`${g.serverId}:${g.name}`).digest("hex").slice(0, 8)}-${server}`;
    const slug = uniqueSlug(base, (s) => taken.has(s));
    taken.add(slug);
    return { slug, name: g.name, serverId: g.serverId, serverName: g.serverName, memberCount: g.count };
  });
}

export function findGuild(slug: string): { guild: GuildInfo; members: GuildMember[] } | null {
  const guild = listGuilds().find((g) => g.slug === slug);
  if (!guild) {
    return null;
  }
  const rows = db
    .select({
      id: players.id,
      slug: players.slug,
      name: players.name,
      classId: playerProfiles.classId,
      level: playerProfiles.level,
      faction: playerProfiles.faction,
    })
    .from(players)
    .leftJoin(playerProfiles, eq(playerProfiles.playerId, players.id))
    .where(and(eq(players.serverId, guild.serverId), eq(players.guild, guild.name)))
    .all();
  const members = rows
    .map((r) => ({
      id: r.id,
      slug: r.slug,
      name: r.name,
      className: r.classId ? (AION2_CLASS_BY_ID[r.classId] ?? null) : null,
      level: r.level,
      faction: r.faction,
    }))
    .sort((a, b) => (b.level ?? 0) - (a.level ?? 0) || a.name.localeCompare(b.name));
  return { guild, members };
}
