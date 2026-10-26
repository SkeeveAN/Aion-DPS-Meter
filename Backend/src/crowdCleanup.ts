import { and, eq, ne } from "drizzle-orm";
import { db } from "./db/client.js";
import { bosses, encounterParticipants, encounters, instances, playerNcMatch, players, servers } from "./db/schema.js";
import { serverOfName } from "./content/aion2Servers.js";
import { allowedServersOfEncounter, plausibleServersOf } from "./crowdRule.js";
import { linkPlayerGuild } from "./guilds.js";
import { noteClientServer, serverRowOf } from "./ncMatch.js";
import { dropNcData } from "./portraitService.js";

// Takes the server from every character whose server breaks the world boss rule (crowdRule.ts) in a fight he took part in (or is a server of the other faction), and files him under the one server his fights
// allow - when they allow exactly one. What NC had said about him goes (it is the data of a namesake), and NC is asked again with the rule in place (ncMatch.ts).

export type CrowdViolator = { playerId: number; name: string; serverRowId: number | null; ncServerId: number | null; fights: number[] };
export type CrowdCleanupReport = {
  /** Players moved (or to be moved) to the one server their fights allow. */
  fixed: { playerId: number; name: string; from: number | null; to: number }[];
  /** The row of the same name already exists on that server: left alone, to be decided. */
  duplicate: { playerId: number; name: string; to: number }[];
  /** His fights allow no server or several: left alone, to be decided. */
  undecided: { playerId: number; name: string; ncServerId: number | null; candidates: number[] }[];
};

/** The NC server id of a servers-table row ("Europe - Kromede" or the label "Name [2316]"). */
function ncIdOf(displayName: string | null): number | null {
  if (displayName === null) {
    return null;
  }
  const label = /\[(\d+)\]/.exec(displayName);
  return label ? Number(label[1]) : (serverOfName(displayName)?.id ?? null);
}

/** Every character filed under a server that one of his world boss fights does not allow. */
export function findCrowdViolators(): CrowdViolator[] {
  const found = new Map<number, CrowdViolator>();
  const fights = db
    .select({ id: encounters.id })
    .from(encounters)
    .innerJoin(bosses, eq(bosses.id, encounters.bossId))
    .innerJoin(instances, eq(instances.id, bosses.instanceId))
    .where(eq(instances.category, "worldboss"))
    .all();
  for (const { id } of fights) {
    const allowed = allowedServersOfEncounter(id);
    if (!allowed) {
      continue;
    }
    const homeFaction = Math.floor([...allowed.servers][0] / 1000);
    const rows = db
      .select({ playerId: players.id, name: players.name, faction: players.faction, serverRowId: players.serverId, serverName: servers.displayName })
      .from(encounterParticipants)
      .innerJoin(players, eq(players.id, encounterParticipants.playerId))
      .leftJoin(servers, eq(servers.id, players.serverId))
      .where(eq(encounterParticipants.encounterId, id))
      .all();
    for (const r of rows) {
      const nc = ncIdOf(r.serverName);
      // A server of the other faction than the player's own is wrong whatever the fight allows: Asmodians do not live on Elyos servers.
      const wrongFaction = nc !== null && r.faction !== "" && Math.floor(nc / 1000) !== (r.faction === "Elyos" ? 1 : 2);
      if (nc === null || (!wrongFaction && (allowed.servers.has(nc) || (!allowed.pairKnown && Math.floor(nc / 1000) !== homeFaction)))) {
        continue;
      }
      const entry = found.get(r.playerId) ?? { playerId: r.playerId, name: r.name, serverRowId: r.serverRowId, ncServerId: nc, fights: [] };
      entry.fights.push(id);
      found.set(r.playerId, entry);
    }
  }
  return [...found.values()];
}

/**
 * Applies the rule to every character found. With `apply` false nothing is written: the report says what would be done.
 * A character that is moved loses his NC data (the data of a namesake) and his match; he is filed as "client" and asked at NC again at once.
 */
export function cleanUpCrowdServers(apply: boolean, now = Date.now()): CrowdCleanupReport {
  const report: CrowdCleanupReport = { fixed: [], duplicate: [], undecided: [] };
  for (const v of findCrowdViolators()) {
    const plausible = plausibleServersOf(v.playerId);
    if (plausible === null || plausible.size !== 1) {
      report.undecided.push({ playerId: v.playerId, name: v.name, ncServerId: v.ncServerId, candidates: [...(plausible ?? [])] });
      continue;
    }
    const target = [...plausible][0];
    const row = db.select({ nameNormalized: players.nameNormalized }).from(players).where(eq(players.id, v.playerId)).get();
    const targetRow = existingServerRow(target);
    const taken =
      row && targetRow !== null
        ? db.select({ id: players.id }).from(players).where(and(eq(players.serverId, targetRow), eq(players.nameNormalized, row.nameNormalized), ne(players.id, v.playerId))).get()
        : undefined;
    if (taken) {
      report.duplicate.push({ playerId: v.playerId, name: v.name, to: target });
      continue;
    }
    report.fixed.push({ playerId: v.playerId, name: v.name, from: v.ncServerId, to: target });
    if (!apply) {
      continue;
    }
    db.transaction(() => {
      db.update(players).set({ serverId: serverRowOf(target) }).where(eq(players.id, v.playerId)).run();
      dropNcData(v.playerId);
      db.delete(playerNcMatch).where(eq(playerNcMatch.playerId, v.playerId)).run();
      noteClientServer(v.playerId, target, now);
      linkPlayerGuild(v.playerId);
    });
  }
  return report;
}

/** The servers-table row of an NC server id, null when there is none yet (nobody can be filed under it then). */
function existingServerRow(ncServerId: number): number | null {
  const all = db.select({ id: servers.id, name: servers.displayName }).from(servers).all();
  return all.find((s) => ncIdOf(s.name) === ncServerId)?.id ?? null;
}
