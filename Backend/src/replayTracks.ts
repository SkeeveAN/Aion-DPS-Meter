import { gunzipSync, gzipSync } from "node:zlib";
import { and, eq, inArray } from "drizzle-orm";
import { z } from "zod";
import { db } from "./db/client.js";
import { bossNpcIds, encounterReplayTracks, encounters, servers } from "./db/schema.js";

/**
 * The "Upload for the replay" of the client: the paths (a point every second or so, centimetres) of the players, the boss and the other monsters of one boss fight. The replay
 * itself is only in the client; the server keeps the paths so a client that saw a player only now and then can fetch what the other clients of the fight saw. The paths of other
 * players only come when their uploader switched on "data of other players" (the client leaves them out otherwise); the server does not look further.
 */
const MAX_POINTS = 6000;

export const replayUploadSchema = z.object({
  clientVersion: z.string().max(40).default(""),
  game: z.literal("aion2"),
  serverFingerprint: z.string().trim().min(1).max(64),
  /** The fight on the server, as the boss upload answered it; without it the fight is looked up by boss and time. */
  encounterId: z.number().int().positive().optional(),
  bossNpcId: z.number().int().positive().max(2_000_000_000).optional(),
  startedAt: z.string().max(40),
  endedAt: z.string().max(40),
  tracks: z
    .array(
      z
        .object({
          kind: z.enum(["player", "boss", "add"]),
          name: z.string().trim().min(1).max(64),
          className: z.string().max(32).optional(),
          isSelf: z.boolean().default(false),
          /** When the first point was taken (UTC); t counts from it in milliseconds. */
          startedAt: z.string().max(40),
          t: z.array(z.number().int().min(0).max(14_400_000)).min(1).max(MAX_POINTS),
          x: z.array(z.number().int().min(-2_000_000).max(2_000_000)).min(1).max(MAX_POINTS),
          y: z.array(z.number().int().min(-2_000_000).max(2_000_000)).min(1).max(MAX_POINTS),
        })
        .refine((tr) => tr.t.length === tr.x.length && tr.t.length === tr.y.length, { message: "replay track: t, x and y must have the same length" }),
    )
    .min(1)
    .max(80),
});

export type ReplayUpload = z.infer<typeof replayUploadSchema>;

const START_TOLERANCE_MS = 60_000;

/** The fight on the server for an upload: the id the client got from the boss upload, else the boss, server and start time. */
export function findEncounterForReplay(p: Pick<ReplayUpload, "serverFingerprint" | "encounterId" | "bossNpcId" | "startedAt">): number | null {
  const server = db.select({ id: servers.id }).from(servers).where(eq(servers.fingerprint, p.serverFingerprint)).get();
  if (!server) {
    return null;
  }
  if (p.encounterId !== undefined) {
    const own = db.select({ id: encounters.id }).from(encounters).where(and(eq(encounters.id, p.encounterId), eq(encounters.serverId, server.id))).get();
    return own?.id ?? null;
  }
  if (p.bossNpcId === undefined) {
    return null;
  }
  const boss = db.select({ bossId: bossNpcIds.bossId }).from(bossNpcIds).where(eq(bossNpcIds.npcId, p.bossNpcId)).get();
  if (!boss) {
    return null;
  }
  const startMs = Date.parse(p.startedAt);
  const near = db
    .select({ id: encounters.id, startedAt: encounters.startedAt })
    .from(encounters)
    .where(and(eq(encounters.serverId, server.id), eq(encounters.bossId, boss.bossId)))
    .all()
    .filter((e) => Math.abs(Date.parse(e.startedAt) - startMs) <= START_TOLERANCE_MS)
    .sort((a, b) => Math.abs(Date.parse(a.startedAt) - startMs) - Math.abs(Date.parse(b.startedAt) - startMs));
  return near[0]?.id ?? null;
}

/** Stores (or replaces) the paths of one uploader for a fight; returns how many paths were kept. */
export function storeReplayTracks(encounterId: number, uploaderHash: string, tracks: ReplayUpload["tracks"]): number {
  let kept = 0;
  for (const tr of tracks) {
    const startMs = Date.parse(tr.startedAt);
    if (!Number.isFinite(startMs)) {
      continue;
    }
    const data = gzipSync(Buffer.from(JSON.stringify({ t: tr.t, x: tr.x, y: tr.y }), "utf8"), { level: 9 });
    db.insert(encounterReplayTracks)
      .values({ encounterId, uploaderHash, kind: tr.kind, name: tr.name, className: tr.className ?? null, isSelf: tr.isSelf, startMs, points: tr.t.length, dataGz: data })
      .onConflictDoUpdate({
        target: [encounterReplayTracks.encounterId, encounterReplayTracks.uploaderHash, encounterReplayTracks.kind, encounterReplayTracks.name],
        set: { className: tr.className ?? null, isSelf: tr.isSelf, startMs, points: tr.t.length, dataGz: data },
      })
      .run();
    kept++;
  }
  return kept;
}

export interface MergedTrack {
  kind: "player" | "boss" | "add";
  name: string;
  className: string | null;
  /** Epoch milliseconds of the first point; t counts from it. */
  startMs: number;
  t: number[];
  x: number[];
  y: number[];
  /** How many uploaders' paths are in it. */
  sources: number;
}

/** Two points of one entity closer in time than this are the same reading seen by two clients. */
const SAME_READING_MS = 250;

/** The paths of a fight, the paths of several uploaders of one entity merged into one (all points in time order, a reading two clients saw once). */
export function mergedReplayTracks(encounterId: number): MergedTrack[] {
  const rows = db.select().from(encounterReplayTracks).where(eq(encounterReplayTracks.encounterId, encounterId)).all();
  const groups = new Map<string, typeof rows>();
  for (const r of rows) {
    const key = `${r.kind}\u0000${r.name}`;
    (groups.get(key) ?? groups.set(key, []).get(key)!).push(r);
  }
  const out: MergedTrack[] = [];
  for (const group of groups.values()) {
    const points: [number, number, number][] = [];
    for (const r of group) {
      const d = JSON.parse(gunzipSync(r.dataGz).toString("utf8")) as { t: number[]; x: number[]; y: number[] };
      for (let i = 0; i < d.t.length; i++) {
        points.push([r.startMs + d.t[i]!, d.x[i]!, d.y[i]!]);
      }
    }
    points.sort((a, b) => a[0] - b[0]);
    const kept: [number, number, number][] = [];
    for (const p of points) {
      if (kept.length === 0 || p[0] - kept[kept.length - 1]![0] >= SAME_READING_MS) {
        kept.push(p);
      }
    }
    const first = kept[0]![0];
    out.push({
      kind: group[0]!.kind as MergedTrack["kind"],
      name: group[0]!.name,
      className: group.find((g) => g.className)?.className ?? null,
      startMs: first,
      t: kept.map((p) => p[0] - first),
      x: kept.map((p) => p[1]),
      y: kept.map((p) => p[2]),
      sources: group.length,
    });
  }
  return out.sort((a, b) => a.kind.localeCompare(b.kind) || a.name.localeCompare(b.name));
}

/** The ids of the fights that have paths (for the page: whether a replay can be fetched). */
export function encountersWithReplay(ids: number[]): Set<number> {
  if (ids.length === 0) {
    return new Set();
  }
  return new Set(db.selectDistinct({ id: encounterReplayTracks.encounterId }).from(encounterReplayTracks).where(inArray(encounterReplayTracks.encounterId, ids)).all().map((r) => r.id));
}
