import { and, eq, inArray, like } from "drizzle-orm";
import { db } from "./db/client.js";
import { players, playerNcMatch, servers, uploads } from "./db/schema.js";
import { registerUploadPlayers } from "./matching/merge.js";
import { shardOf } from "./ncMatch.js";
import { approveUpload } from "./uploadReview.js";
import { uploadSchema, type UploadPayload } from "./uploadSchema.js";
import { clearPageCache } from "./seo/cache.js";

/**
 * A run counts only when NC's character search has vouched for every player in it:
 *  - pass: all players verified (a region NC cannot search - Taiwan, Korea - passes when the faction is known),
 *  - fail: one player failed the check (no character of that name at NC - status 'none', or 'client' after NC found nobody on his server - or one that contradicts what the client said): the whole run is turned away,
 *  - wait: a check is still open (new player, several candidates, NC unreachable): the upload waits and is merged when the checks are in.
 * Only the NC outcomes 'none' and 'conflict' fail a player; an unclear name stays open until the next try.
 */
export type Gate = "pass" | "fail" | "wait";

export const NC_PENDING = "nc_pending";
export const PLAYER_CHECK_FAILED = "player_check_failed";

export function gateOfPlayers(ids: number[], now = Date.now()): Gate {
  if (ids.length === 0) {
    return "pass";
  }
  const rows = db
    .select({ faction: players.faction, serverName: servers.displayName, status: playerNcMatch.status, nextTryAt: playerNcMatch.nextTryAt, candidates: playerNcMatch.candidates })
    .from(players)
    .leftJoin(servers, eq(players.serverId, servers.id))
    .leftJoin(playerNcMatch, eq(playerNcMatch.playerId, players.id))
    .where(inArray(players.id, ids))
    .all();
  let open = false;
  for (const r of rows) {
    if (r.status === "verified") {
      continue;
    }
    if (r.status === "none" || r.status === "conflict") {
      return "fail";
    }
    if (shardOf(r.serverName) === null) {
      if (r.faction === "") {
        return "fail";
      }
      continue;
    }
    // 'client': his own client named his server. Before NC has looked (the next try is due) the check is open; after it, "no candidate at NC"
    // is a failed check like 'none' (the search ran on his server only), several candidates stay open.
    if (r.status === "client" && r.nextTryAt !== null && r.nextTryAt > now && r.candidates === 0) {
      return "fail";
    }
    open = true;
  }
  return open ? "wait" : "pass";
}

/** Files the players of the upload and judges the run by them. */
export function gateUpload(payload: UploadPayload): Gate {
  return gateOfPlayers(registerUploadPlayers(payload));
}

/** Looks at every upload that waits for NC: merges the ones whose players are all verified, turns away the ones with a failed player. Returns what it settled. */
export function settleNcPending(): { merged: number; failed: number } {
  const rows = db
    .select()
    .from(uploads)
    .where(and(eq(uploads.status, "pending"), like(uploads.reviewReason, `%${NC_PENDING}%`)))
    .orderBy(uploads.id)
    .limit(200)
    .all();
  let merged = 0;
  let failed = 0;
  for (const row of rows) {
    try {
      const parsed = uploadSchema.safeParse(JSON.parse(row.rawPayloadJson));
      if (!parsed.success) {
        continue;
      }
      const gate = gateUpload(parsed.data);
      if (gate === "pass") {
        approveUpload(row.id);
        merged++;
      } else if (gate === "fail") {
        db.update(uploads)
          .set({ status: "rejected", reviewReason: JSON.stringify([{ code: PLAYER_CHECK_FAILED, detail: "a player failed the NC check" }]), reviewedAt: new Date().toISOString() })
          .where(eq(uploads.id, row.id))
          .run();
        failed++;
      }
    } catch {
      // stays waiting; the next round tries again
    }
  }
  if (merged + failed > 0) {
    clearPageCache();
  }
  return { merged, failed };
}

export function startNcGateSweep(intervalMs = 30_000): void {
  if (process.env.NODE_TEST_CONTEXT) {
    return;
  }
  setInterval(() => {
    try {
      settleNcPending();
    } catch {
      // next round
    }
  }, intervalMs).unref();
}
