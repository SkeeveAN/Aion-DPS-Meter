import { createHash } from "node:crypto";
import { and, asc, eq, isNull, like, sql } from "drizzle-orm";
import { db } from "./db/client.js";
import { guilds, playerProfiles, players, servers } from "./db/schema.js";
import { AION2_CLASS_BY_ID } from "./constants.js";
import { averageItemLevelOf } from "./profile.js";
import { factionOfServerName } from "./factions.js";
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
  /** The faction of the legion's server ("" when the server is unknown). */
  faction: string;
  /** Only with listGuilds({ stats: true }): the members' classes (English names) and the average of their average item levels. */
  classes?: Record<string, number>;
  avgItemLevel?: number | null;
}

export interface GuildMember {
  id: number;
  slug: string | null;
  name: string;
  className: string | null;
  level: number | null;
  /** The game's own gear score; only known for players whose character window an uploader opened. */
  gearScore: number | null;
  /** Our own average item level of the worn gear. */
  avgItemLevel: number | null;
}

const normalizeGuild = (name: string) => name.trim().toLowerCase();

/** The legion row of a name on a server, created when it is new. */
export function ensureGuild(name: string, serverId: number): number {
  const trimmed = name.trim();
  const norm = normalizeGuild(trimmed);
  const found = db.select({ id: guilds.id }).from(guilds).where(and(eq(guilds.serverId, serverId), eq(guilds.nameNormalized, norm))).get();
  if (found) {
    return found.id;
  }
  return Number(db.insert(guilds).values({ name: trimmed, nameNormalized: norm, serverId }).run().lastInsertRowid);
}

/**
 * Points a player at the legion row that matches his legion name and his server. A legion that only this player belonged to
 * moves with him (it keeps its row and its link); otherwise the legion of that name on his server is used or created.
 * Returns true when the link changed.
 */
export function linkPlayerGuild(playerId: number): boolean {
  const p = db.select({ guild: players.guild, serverId: players.serverId, guildId: players.guildId }).from(players).where(eq(players.id, playerId)).get();
  if (!p) {
    return false;
  }
  const name = (p.guild ?? "").trim();
  if (!name || p.serverId === null) {
    if (p.guildId !== null) {
      db.update(players).set({ guildId: null }).where(eq(players.id, playerId)).run();
      return true;
    }
    return false;
  }
  const norm = normalizeGuild(name);
  const target = db.select({ id: guilds.id }).from(guilds).where(and(eq(guilds.serverId, p.serverId), eq(guilds.nameNormalized, norm))).get();
  if (target) {
    if (p.guildId === target.id) {
      return false;
    }
    db.update(players).set({ guildId: target.id }).where(eq(players.id, playerId)).run();
    return true;
  }
  const current = p.guildId !== null ? db.select().from(guilds).where(eq(guilds.id, p.guildId)).get() : undefined;
  if (current && current.nameNormalized === norm) {
    const members = db.select({ n: sql<number>`count(*)` }).from(players).where(eq(players.guildId, current.id)).get()?.n ?? 0;
    if (members <= 1) {
      db.update(guilds).set({ serverId: p.serverId }).where(eq(guilds.id, current.id)).run();
      return true;
    }
  }
  db.update(players).set({ guildId: ensureGuild(name, p.serverId) }).where(eq(players.id, playerId)).run();
  return true;
}

/** Makes every player's legion link match his legion name and server again (after moves done outside the upload path). */
export function syncGuilds(): number {
  // The migration normalised names with SQLite's lower(), which only handles ASCII; bring every row to the same spelling as the code uses.
  for (const g of db.select({ id: guilds.id, name: guilds.name, norm: guilds.nameNormalized }).from(guilds).all()) {
    const norm = normalizeGuild(g.name);
    if (norm !== g.norm) {
      try {
        db.update(guilds).set({ nameNormalized: norm }).where(eq(guilds.id, g.id)).run();
      } catch {
        // Another row of the same server already has that spelling: the players of this one are linked to that one below.
      }
    }
  }
  const rows = db
    .select({ id: players.id, guild: players.guild, serverId: players.serverId, guildId: players.guildId, gServer: guilds.serverId, gNorm: guilds.nameNormalized })
    .from(players)
    .leftJoin(guilds, eq(players.guildId, guilds.id))
    .orderBy(asc(players.id))
    .all();
  let changed = 0;
  for (const r of rows) {
    const name = (r.guild ?? "").trim();
    const ok = name ? r.guildId !== null && r.gServer === r.serverId && r.gNorm === normalizeGuild(name) : r.guildId === null;
    if (!ok && linkPlayerGuild(r.id)) {
      changed++;
    }
  }
  return changed;
}

/** Gives every legion without a link its slug "<legion>-<server>" (a Latin name reads in the address, any other falls back to a short hash). Kept once set. */
export function assignGuildSlugs(): number {
  const missing = db
    .select({ id: guilds.id, name: guilds.name, serverId: guilds.serverId, serverName: servers.displayName })
    .from(guilds)
    .innerJoin(servers, eq(guilds.serverId, servers.id))
    .where(isNull(guilds.slug))
    .all()
    // Sorted first, so a clash of two names that slugify alike always resolves the same way.
    .sort((a, b) => a.serverId - b.serverId || a.name.localeCompare(b.name));
  if (missing.length === 0) {
    return 0;
  }
  const taken = new Set(db.select({ slug: guilds.slug }).from(guilds).all().map((r) => r.slug).filter((x): x is string => x !== null));
  for (const g of missing) {
    const server = slugify(g.serverName ?? "") || `server-${g.serverId}`;
    const base = slugify(g.name)
      ? `${slugify(g.name)}-${server}`
      : `legion-${createHash("sha1").update(`${g.serverId}:${g.name}`).digest("hex").slice(0, 8)}-${server}`;
    const slug = uniqueSlug(base, (s) => taken.has(s));
    taken.add(slug);
    db.update(guilds).set({ slug }).where(eq(guilds.id, g.id)).run();
  }
  return missing.length;
}

/** Every legion that has members, with its stored slug. */
export function listGuilds(options: { stats?: boolean } = {}): GuildInfo[] {
  assignGuildSlugs();
  const rows = db
    .select({
      gid: guilds.id,
      slug: guilds.slug,
      name: guilds.name,
      serverId: guilds.serverId,
      serverName: servers.displayName,
      classId: playerProfiles.classId,
      gearJson: playerProfiles.gearJson,
    })
    .from(players)
    .innerJoin(guilds, eq(players.guildId, guilds.id))
    .innerJoin(servers, eq(guilds.serverId, servers.id))
    .leftJoin(playerProfiles, eq(playerProfiles.playerId, players.id))
    .where(like(servers.fingerprint, "aion2:%"))
    .all();

  const counts = new Map<number, { slug: string; name: string; serverId: number; serverName: string; count: number; classes: Record<string, number>; levels: number[] }>();
  for (const r of rows) {
    const entry = counts.get(r.gid) ?? { slug: r.slug ?? "", name: r.name, serverId: r.serverId, serverName: r.serverName ?? "", count: 0, classes: {}, levels: [] };
    entry.count += 1;
    if (options.stats) {
      const cls = r.classId ? AION2_CLASS_BY_ID[r.classId] : undefined;
      if (cls) {
        entry.classes[cls] = (entry.classes[cls] ?? 0) + 1;
      }
      const level = r.gearJson ? averageItemLevelOf(r.gearJson) : null;
      if (level) {
        entry.levels.push(level);
      }
    }
    counts.set(r.gid, entry);
  }

  return [...counts.values()]
    .sort((a, b) => a.serverId - b.serverId || a.name.localeCompare(b.name))
    .map((g) => {
      const info: GuildInfo = { slug: g.slug, name: g.name, serverId: g.serverId, serverName: g.serverName, memberCount: g.count, faction: factionOfServerName(g.serverName) };
      if (options.stats) {
        info.classes = g.classes;
        info.avgItemLevel = g.levels.length > 0 ? Math.round(g.levels.reduce((a, b) => a + b, 0) / g.levels.length) : null;
      }
      return info;
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
      gearJson: playerProfiles.gearJson,
      gearScore: playerProfiles.gearScore,
    })
    .from(players)
    .leftJoin(playerProfiles, eq(playerProfiles.playerId, players.id))
    .innerJoin(guilds, eq(players.guildId, guilds.id))
    .where(and(eq(guilds.serverId, guild.serverId), eq(guilds.nameNormalized, normalizeGuild(guild.name))))
    .all();
  const members = rows
    .map((r) => ({
      id: r.id,
      slug: r.slug,
      name: r.name,
      className: r.classId ? (AION2_CLASS_BY_ID[r.classId] ?? null) : null,
      level: r.level,
      gearScore: r.gearScore,
      avgItemLevel: r.gearJson ? averageItemLevelOf(r.gearJson) : null,
    }))
    .sort((a, b) => (b.level ?? 0) - (a.level ?? 0) || a.name.localeCompare(b.name));
  return { guild, members };
}
