import { createHash } from "node:crypto";
import { and, desc, eq, isNull, sql } from "drizzle-orm";
import { db } from "./db/client.js";
import { playerProfiles, profileSetups, setupVotes } from "./db/schema.js";

/**
 * Thumbs on the Daevanion boards and on the skill build of a character.
 *
 * What is rated is a STATE: all boards of the character together ("board"), or the skills the character has chosen ("skills"). The profile row keeps only the
 * newest reading, so the states live in profile_setups: whenever a profile is stored, `syncSetups` compares the new state with the current one.
 * - the same state: nothing happens;
 * - a new state while the current one has thumbs: the current one is kept as an earlier state (superseded_at set) and a new current one starts at zero,
 *   so the thumbs stay with the state they were given to;
 * - a new state while the current one has no thumb: it is overwritten in place, so only rated states pile up;
 * - a state that comes back (the player returns to an earlier build): its old row, with its thumbs, becomes current again.
 */
export type SetupKind = "board" | "skills";
export const SETUP_KINDS: SetupKind[] = ["board", "skills"];

/** At most this many thumbs from one network address on one state: a household or a legion's house shares an address, a script does not get a crowd out of it. */
export const MAX_THUMBS_PER_ADDRESS = 3;

type StoredSkill = { id: number; level?: number; baseLevel?: number; stigma?: boolean; equipped?: boolean; stones?: number[] };

/**
 * The part of a stored list that makes up the state, in a fixed order, or null when there is nothing to rate.
 * Boards: every board with its active node ids. Skills: what the character CHOSE (learned level, stigma slot, specialisation stones); the effective level is left
 * out, because a change of equipment moves it and would start a new state without the player having changed the build.
 */
export function normaliseSetup(kind: SetupKind, json: string | null | undefined): unknown | null {
  let list: unknown;
  try {
    list = JSON.parse(json ?? "[]");
  } catch {
    return null;
  }
  if (!Array.isArray(list) || list.length === 0) {
    return null;
  }
  if (kind === "board") {
    const boards = (list as { board?: number; nodes?: number[] }[])
      .filter((b) => typeof b?.board === "number" && Array.isArray(b.nodes))
      .map((b) => ({ board: b.board as number, nodes: [...(b.nodes as number[])].sort((x, y) => x - y) }))
      .sort((x, y) => x.board - y.board);
    return boards.length > 0 ? boards : null;
  }
  const skills = (list as StoredSkill[])
    .filter((s) => typeof s?.id === "number" && ((s.baseLevel ?? 0) > 0 || s.equipped === true || (s.stones?.length ?? 0) > 0))
    .map((s) => ({ id: s.id, base: s.baseLevel ?? 0, stigma: s.stigma === true, equipped: s.equipped === true, stones: [...(s.stones ?? [])].sort((x, y) => x - y) }))
    .sort((x, y) => x.id - y.id);
  return skills.length > 0 ? skills : null;
}

export function setupHash(kind: SetupKind, json: string | null | undefined): string | null {
  const normal = normaliseSetup(kind, json);
  return normal === null ? null : createHash("sha256").update(`${kind}|${JSON.stringify(normal)}`).digest("hex");
}

const stamp = (now: number): string => new Date(now).toISOString().slice(0, 19).replace("T", " ");

/** Records the state a profile now has; see the description at the top. Returns the id of the row that is current afterwards (null when there is nothing to rate). */
export function recordSetup(playerId: number, kind: SetupKind, dataJson: string, classId: number | null, now = Date.now()): number | null {
  const hash = setupHash(kind, dataJson);
  if (hash === null) {
    return null;
  }
  return db.transaction((tx) => {
    const current = tx
      .select()
      .from(profileSetups)
      .where(and(eq(profileSetups.playerId, playerId), eq(profileSetups.kind, kind), isNull(profileSetups.supersededAt)))
      .get();
    if (current?.hash === hash) {
      return current.id;
    }
    const same = tx
      .select()
      .from(profileSetups)
      .where(and(eq(profileSetups.playerId, playerId), eq(profileSetups.kind, kind), eq(profileSetups.hash, hash)))
      .get();
    // the old current state goes: kept as an earlier one when it has thumbs, dropped when it has none (nobody voted for it, nothing refers to it)
    const retire = () => {
      if (!current) {
        return;
      }
      if (current.thumbs > 0) {
        tx.update(profileSetups).set({ supersededAt: stamp(now) }).where(eq(profileSetups.id, current.id)).run();
      }
    };
    if (same) {
      // back to an earlier state: its row, with its thumbs, is current again
      if (current && current.thumbs === 0) {
        tx.delete(profileSetups).where(eq(profileSetups.id, current.id)).run();
      } else {
        retire();
      }
      tx.update(profileSetups).set({ supersededAt: null, updatedAt: stamp(now), classId }).where(eq(profileSetups.id, same.id)).run();
      return same.id;
    }
    if (current && current.thumbs === 0) {
      tx.update(profileSetups).set({ hash, dataJson, classId, updatedAt: stamp(now), createdAt: stamp(now) }).where(eq(profileSetups.id, current.id)).run();
      return current.id;
    }
    retire();
    return tx.insert(profileSetups).values({ playerId, kind, hash, dataJson, classId, createdAt: stamp(now), updatedAt: stamp(now) }).returning({ id: profileSetups.id }).get().id;
  });
}

/** Records the boards and the skills of the stored profile of a player. Called after every write of a profile; a failure here must never fail an upload. */
export function syncSetups(playerId: number, now = Date.now()): void {
  try {
    const row = db.select().from(playerProfiles).where(eq(playerProfiles.playerId, playerId)).get();
    if (!row) {
      return;
    }
    recordSetup(playerId, "board", row.daevanionJson, row.classId, now);
    recordSetup(playerId, "skills", row.skillsJson, row.classId, now);
  } catch (error) {
    console.error(`syncSetups(${playerId}) failed`, error);
  }
}

// ---- reading -------------------------------------------------------------------------------------------------------------------------------------------------

export interface SetupInfo {
  id: number;
  thumbs: number;
  /** The reader's own thumb is on it. */
  voted: boolean;
  createdAt: string;
  supersededAt: string | null;
}

export interface SetupState {
  current: SetupInfo | null;
  /** States that are not current any more but kept their thumbs, newest first. */
  earlier: SetupInfo[];
}

const info = (row: typeof profileSetups.$inferSelect, voted: Set<number>): SetupInfo => ({
  id: row.id,
  thumbs: row.thumbs,
  voted: voted.has(row.id),
  createdAt: row.createdAt,
  supersededAt: row.supersededAt,
});

/** The thumbs of one player: for each kind the current state and the earlier rated ones. `voterHash` marks the reader's own thumbs (null = unknown reader). */
export function setupsOfPlayer(playerId: number, voterHash: string | null): Record<SetupKind, SetupState> {
  // A profile stored before this feature has no row yet: make it now (cheap check, writes only once per player and kind).
  const hasCurrent = new Set(
    db
      .select({ kind: profileSetups.kind })
      .from(profileSetups)
      .where(and(eq(profileSetups.playerId, playerId), isNull(profileSetups.supersededAt)))
      .all()
      .map((r) => r.kind),
  );
  if (hasCurrent.size < SETUP_KINDS.length) {
    syncSetups(playerId);
  }
  const rows = db
    .select()
    .from(profileSetups)
    .where(eq(profileSetups.playerId, playerId))
    .orderBy(desc(profileSetups.updatedAt), desc(profileSetups.id))
    .all();
  const voted = new Set<number>();
  if (voterHash && rows.length > 0) {
    for (const v of db.select({ id: setupVotes.setupId }).from(setupVotes).where(eq(setupVotes.voterHash, voterHash)).all()) {
      voted.add(v.id);
    }
  }
  const out = {} as Record<SetupKind, SetupState>;
  for (const kind of SETUP_KINDS) {
    const mine = rows.filter((r) => r.kind === kind);
    const current = mine.find((r) => r.supersededAt === null);
    out[kind] = {
      current: current ? info(current, voted) : null,
      earlier: mine
        .filter((r) => r.supersededAt !== null && r.thumbs > 0)
        .sort((a, b) => (b.supersededAt ?? "").localeCompare(a.supersededAt ?? "") || b.id - a.id)
        .map((r) => info(r, voted)),
    };
  }
  return out;
}

export function setupById(id: number) {
  return db.select().from(profileSetups).where(eq(profileSetups.id, id)).get() ?? null;
}

// ---- voting --------------------------------------------------------------------------------------------------------------------------------------------------

export type VoteResult =
  | { ok: true; thumbs: number; voted: boolean }
  | { ok: false; error: "not_found" | "superseded" | "address_limit" | "nothing_to_transfer" };

const recount = (tx: Pick<typeof db, "select" | "update">, setupId: number): number => {
  const n = tx.select({ n: sql<number>`count(*)` }).from(setupVotes).where(eq(setupVotes.setupId, setupId)).get()?.n ?? 0;
  tx.update(profileSetups).set({ thumbs: n }).where(eq(profileSetups.id, setupId)).run();
  return n;
};

/** Puts the reader's thumb on the CURRENT state `setupId`. A second thumb of the same browser does nothing; more than MAX_THUMBS_PER_ADDRESS from one address are refused. */
export function addThumb(setupId: number, voterHash: string, ipHash: string): VoteResult {
  return db.transaction((tx) => {
    const row = tx.select().from(profileSetups).where(eq(profileSetups.id, setupId)).get();
    if (!row) {
      return { ok: false, error: "not_found" } as const;
    }
    if (row.supersededAt !== null) {
      return { ok: false, error: "superseded" } as const;
    }
    const already = tx.select().from(setupVotes).where(and(eq(setupVotes.setupId, setupId), eq(setupVotes.voterHash, voterHash))).get();
    if (already) {
      return { ok: true, thumbs: row.thumbs, voted: true } as const;
    }
    const fromAddress = tx.select({ n: sql<number>`count(*)` }).from(setupVotes).where(and(eq(setupVotes.setupId, setupId), eq(setupVotes.ipHash, ipHash))).get()?.n ?? 0;
    if (fromAddress >= MAX_THUMBS_PER_ADDRESS) {
      return { ok: false, error: "address_limit" } as const;
    }
    tx.insert(setupVotes).values({ setupId, voterHash, ipHash }).run();
    return { ok: true, thumbs: recount(tx, setupId), voted: true } as const;
  });
}

/** Takes the reader's thumb back. An earlier state that has no thumb left is deleted: nothing refers to it any more. */
export function removeThumb(setupId: number, voterHash: string): VoteResult {
  return db.transaction((tx) => {
    const row = tx.select().from(profileSetups).where(eq(profileSetups.id, setupId)).get();
    if (!row) {
      return { ok: false, error: "not_found" } as const;
    }
    tx.delete(setupVotes).where(and(eq(setupVotes.setupId, setupId), eq(setupVotes.voterHash, voterHash))).run();
    const thumbs = recount(tx, setupId);
    if (thumbs === 0 && row.supersededAt !== null) {
      tx.delete(profileSetups).where(eq(profileSetups.id, setupId)).run();
    }
    return { ok: true, thumbs, voted: false } as const;
  });
}

/** Moves the reader's thumb from an earlier state to the character's current state of the same kind. */
export function moveThumb(fromId: number, voterHash: string, ipHash: string): (VoteResult & { toId?: number; fromThumbs?: number }) {
  const from = setupById(fromId);
  if (!from) {
    return { ok: false, error: "not_found" };
  }
  const mine = db.select().from(setupVotes).where(and(eq(setupVotes.setupId, fromId), eq(setupVotes.voterHash, voterHash))).get();
  if (!mine) {
    return { ok: false, error: "nothing_to_transfer" };
  }
  const to = db
    .select()
    .from(profileSetups)
    .where(and(eq(profileSetups.playerId, from.playerId), eq(profileSetups.kind, from.kind), isNull(profileSetups.supersededAt)))
    .get();
  if (!to || to.id === fromId) {
    return { ok: false, error: "nothing_to_transfer" };
  }
  const added = addThumb(to.id, voterHash, ipHash);
  if (!added.ok) {
    return added;
  }
  const removed = removeThumb(fromId, voterHash);
  return { ...added, toId: to.id, fromThumbs: removed.ok ? removed.thumbs : 0 };
}
