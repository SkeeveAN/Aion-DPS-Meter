import { sqlite } from "../db/client.js";
import { factionOfServerName } from "../factions.js";
import { linkPlayerGuild } from "../guilds.js";
import { normalizeName } from "./roster.js";

/**
 * Old clients filed every player of an upload under the uploader's server. A newer client sends the
 * server each player announced himself with, and then the row that was filed under the uploader's
 * server is the same person as the one the upload now names - it moves to the right server (keeping
 * its history, profile and link) instead of a second row being started. Only moved when it is certain:
 *
 *  1. the faction contradicts the server it sits on and fits the new one (an Asmodian under an Elyos server),
 *  2. the legion is the same on both sides (both known, equal),
 *  3. the legion of before is known, this upload names no other one, and at least two of the players around him
 *     in this upload were seen with him in at least two earlier fights, all of them of that same legion.
 *
 * Anything else stays as it is: a namesake of another Elyos server must never take over a row.
 */

interface Member {
  name: string;
  norm: string;
  guild: string;
  target: number; // the server the upload names for him
}

interface Row {
  id: number;
  guild: string | null;
}

const rowOf = (serverId: number, norm: string) =>
  sqlite.prepare("select id, guild from players where server_id = ? and name_normalized = ?").get(serverId, norm) as Row | undefined;

const aliasOf = (serverId: number, norm: string) =>
  (sqlite.prepare("select alias_names_normalized a from players where server_id = ? and alias_names_normalized like ?").all(serverId, `%${JSON.stringify(norm)}%`) as { a: string }[]).length > 0;

const serverName = (serverId: number) =>
  (sqlite.prepare("select display_name n from servers where id = ?").get(serverId) as { n: string | null } | undefined)?.n ?? null;

/** The faction a stored player is known to have: from a fight only - the faction field of a profile is no faction (Elyos characters carry 1 and 2 there). */
function factionOfPlayer(playerId: number): string {
  const own = sqlite.prepare("select faction f from players where id = ?").get(playerId) as { f: string } | undefined;
  if (own?.f) {
    return own.f;
  }
  const fight = sqlite
    .prepare("select faction f from encounter_participants where player_id = ? and faction <> '' order by id desc limit 1")
    .get(playerId) as { f: string } | undefined;
  return fight?.f ?? "";
}

const sameGuild = (a: string | null | undefined, b: string | null | undefined) => !!a && !!b && a.trim().toLowerCase() === b.trim().toLowerCase();

/** In how many fights both players took part. */
function sharedFights(a: number, b: number): number {
  return (
    sqlite
      .prepare("select count(*) n from encounter_participants x join encounter_participants y on y.encounter_id = x.encounter_id where x.player_id = ? and y.player_id = ?")
      .get(a, b) as { n: number }
  ).n;
}

export interface RehomeResult {
  moved: string[];
}

/**
 * @param members every player of the upload together with the server the upload names for him (only those that name one)
 * @param uploaderServerId the server the old clients filed everybody under
 */
export function rehomeKnownPlayers(members: { name: string; guild?: string; target: number }[], uploaderServerId: number): RehomeResult {
  const moved: string[] = [];
  const list: Member[] = members
    .filter((m) => m.target !== uploaderServerId)
    .map((m) => ({ name: m.name, norm: normalizeName(m.name), guild: m.guild ?? "", target: m.target }));
  if (list.length === 0) {
    return { moved };
  }

  const uploaderFaction = factionOfServerName(serverName(uploaderServerId));
  const pending = new Set(list.map((m) => m.norm));
  const settled = new Map<string, number>(); // norm -> player id now on the server the upload names

  const settleExisting = (m: Member) => {
    const there = rowOf(m.target, m.norm);
    if (there) {
      settled.set(m.norm, there.id);
      pending.delete(m.norm);
    }
    return there;
  };

  const move = (m: Member, candidate: Row) => {
    if (rowOf(m.target, m.norm) || aliasOf(m.target, m.norm)) {
      return;
    }
    sqlite.prepare("update players set server_id = ? where id = ?").run(m.target, candidate.id);
    linkPlayerGuild(candidate.id);
    settled.set(m.norm, candidate.id);
    pending.delete(m.norm);
    moved.push(m.name);
  };

  // Rules 1 and 2 look at one player alone.
  for (const m of list) {
    if (settleExisting(m)) {
      continue;
    }
    const candidate = rowOf(uploaderServerId, m.norm);
    if (!candidate) {
      pending.delete(m.norm);
      continue;
    }
    const targetFaction = factionOfServerName(serverName(m.target));
    const known = factionOfPlayer(candidate.id);
    const fits = !known || !targetFaction || known === targetFaction;
    if (known && uploaderFaction && targetFaction && known !== uploaderFaction && known === targetFaction) {
      move(m, candidate);
    } else if (fits && sameGuild(candidate.guild, m.guild)) {
      move(m, candidate);
    }
  }

  // Rule 3 needs the others of this upload to be sure first, so it repeats while somebody gets settled.
  for (let round = 0; round < 3 && pending.size > 0; round++) {
    let progress = false;
    for (const m of list.filter((x) => pending.has(x.norm))) {
      const candidate = rowOf(uploaderServerId, m.norm);
      if (!candidate || !candidate.guild || (m.guild && !sameGuild(candidate.guild, m.guild))) {
        continue;
      }
      const targetFaction = factionOfServerName(serverName(m.target));
      const known = factionOfPlayer(candidate.id);
      if (known && targetFaction && known !== targetFaction) {
        continue;
      }
      let mates = 0;
      for (const other of list) {
        const otherId = settled.get(other.norm);
        if (other.norm === m.norm || otherId === undefined) {
          continue;
        }
        const otherRow = sqlite.prepare("select guild from players where id = ?").get(otherId) as { guild: string | null } | undefined;
        if (!sameGuild(otherRow?.guild, candidate.guild)) {
          continue;
        }
        // The mate's history may sit on his old row (filed under the uploader's server) or on the one he has now.
        const mateIds = new Set<number>([otherId]);
        const old = rowOf(uploaderServerId, other.norm);
        if (old) {
          mateIds.add(old.id);
        }
        if ([...mateIds].some((id) => sharedFights(candidate.id, id) >= 2)) {
          mates++;
        }
      }
      if (mates >= 2) {
        move(m, candidate);
        progress = true;
      }
    }
    if (!progress) {
      break;
    }
  }

  return { moved };
}
