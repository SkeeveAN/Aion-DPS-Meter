import { and, eq, isNull } from "drizzle-orm";
import { db } from "./db/client.js";
import { serverCatalog, serverMatchmaking } from "./db/schema.js";
import { load, matchmakingView, refreshMatchmaking, type MatchPair } from "./serverMatchmaking.js";

// The matchmaking pairs of NC's notices (serverMatchmaking.ts) in the database, so the rest of the site can use them. One row per pairing and period:
// a pairing NC no longer lists is closed (valid_to), a new one is opened, so "who was matched with whom when" stays answerable.

type Region = { region: string | null; noticeId: string; pairs: MatchPair[] };

/** Brings the open rows of every region in line with the given lists. A region the notices do not mention is left alone. Returns how many rows were opened and closed. */
export function syncMatchmakingToDb(regions: Region[], now = Date.now()): { opened: number; closed: number } {
  let opened = 0;
  let closed = 0;
  const catalog = new Map(db.select({ id: serverCatalog.id, name: serverCatalog.name }).from(serverCatalog).all().map((r) => [r.name, r.id]));
  db.transaction((tx) => {
    for (const r of regions) {
      if (!r.region || r.pairs.length === 0) {
        continue;
      }
      const key = (elyos: string, asmodian: string) => `${elyos}|${asmodian}`;
      const open = tx.select().from(serverMatchmaking).where(and(eq(serverMatchmaking.region, r.region), isNull(serverMatchmaking.validTo))).all();
      const wanted = new Map(r.pairs.map((p) => [key(p.elyos, p.asmodian), p]));
      const have = new Set<string>();
      for (const row of open) {
        const k = key(row.elyosServer, row.asmodianServer);
        const p = wanted.get(k);
        if (!p || have.has(k)) {
          tx.update(serverMatchmaking).set({ validTo: now }).where(eq(serverMatchmaking.id, row.id)).run();
          closed++;
          continue;
        }
        have.add(k);
        if (row.phase !== p.phase || row.updatedLabel !== p.updated || row.noticeId !== r.noticeId) {
          tx.update(serverMatchmaking).set({ phase: p.phase, updatedLabel: p.updated, noticeId: r.noticeId }).where(eq(serverMatchmaking.id, row.id)).run();
        }
      }
      for (const [k, p] of wanted) {
        if (have.has(k)) {
          continue;
        }
        tx.insert(serverMatchmaking)
          .values({
            region: r.region,
            elyosServer: p.elyos,
            asmodianServer: p.asmodian,
            elyosCatalogId: catalog.get(`${r.region} - ${p.elyos}`) ?? null,
            asmodianCatalogId: catalog.get(`${r.region} - ${p.asmodian}`) ?? null,
            phase: p.phase,
            updatedLabel: p.updated,
            noticeId: r.noticeId,
            validFrom: now,
          })
          .run();
        opened++;
      }
    }
  });
  return { opened, closed };
}

/** The pairings that are current now, optionally of one region. */
export function currentMatches(region?: string) {
  const open = isNull(serverMatchmaking.validTo);
  return db.select().from(serverMatchmaking).where(region ? and(open, eq(serverMatchmaking.region, region)) : open).all();
}

/** The server a server shares its world with right now ("Europe", "Kaisinel" -> "Lumiel"), or null when no current pairing names it. */
export function matchedServer(region: string, server: string): string | null {
  for (const m of currentMatches(region)) {
    if (m.elyosServer === server) return m.asmodianServer;
    if (m.asmodianServer === server) return m.elyosServer;
  }
  return null;
}

/** Off under tests and with NC_MATCH_OFF=1 (like the other NC jobs). Starts after the announcement store has had its first refresh. */
export function startMatchmaking(intervalMs = 30 * 60_000): void {
  load();
  if (process.env.NC_MATCH_OFF === "1" || process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const run = async () => {
    await refreshMatchmaking();
    syncMatchmakingToDb(matchmakingView().regions);
  };
  setTimeout(() => void run().catch(() => undefined), 100_000).unref();
  setInterval(() => void run().catch(() => undefined), intervalMs).unref();
}
