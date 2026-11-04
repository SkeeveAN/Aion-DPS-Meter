import { gunzipSync, gzipSync } from "node:zlib";
import { sql } from "drizzle-orm";
import { db, sqlite } from "./db/client.js";
import { uploads } from "./db/schema.js";
import { backfillChainUsage } from "./chainUsage.js";

/**
 * The raw payload of an upload is only an audit copy once the upload is settled (merged): the facts are in the encounter, player and profile tables.
 * Uploads that wait for a review or were turned away keep the plain text (the review parses it). Every few hours:
 *  - a merged upload older than six hours gets its payload gzipped (checked by unpacking it again), the gzip is written to the second database file
 *    (table arch.payloads, see db/client.ts) and only then the plain column is emptied - the main file keeps nothing of it, which keeps it small;
 *  - the time axes of the participants (encounter_timelines) are gzipped the same way; nothing is lost, the text comes back unchanged;
 *  - the payload of a merged upload without a fight (a profile-only upload) is dropped for good after UPLOAD_PROFILE_PAYLOAD_KEEP_DAYS (default 90).
 * The upload row itself stays, so the counts of the statistics do not change.
 */

/**
 * The raw text of an upload row, whether it is stored plain, as gzip in the row (older rows) or in the archive file; null when it was dropped.
 * The archive is only asked when the row has an id and nothing of the payload is left in it.
 */
export function rawPayloadOf(row: { id?: number; rawPayloadJson: string; rawPayloadGz: Buffer | null }): string | null {
  if (row.rawPayloadJson !== "") {
    return row.rawPayloadJson;
  }
  if (row.rawPayloadGz) {
    return gunzipSync(row.rawPayloadGz).toString("utf8");
  }
  if (row.id !== undefined) {
    const archived = sqlite.prepare("select gz from arch.payloads where upload_id = ?").get(row.id) as { gz: Buffer } | undefined;
    return archived ? gunzipSync(archived.gz).toString("utf8") : null;
  }
  return null;
}

/** The text of a stored time axis, whether it sits plain or as gzip; null when there is none. */
export function timelineJsonOf(row: { dataJson: string; dataGz: Buffer | null }): string | null {
  if (row.dataJson !== "") {
    return row.dataJson;
  }
  return row.dataGz ? gunzipSync(row.dataGz).toString("utf8") : null;
}

/** The gzip of a time axis text, checked by unpacking it again. */
export function packTimeline(json: string): Buffer {
  const gz = gzipSync(Buffer.from(json, "utf8"), { level: 9 });
  if (gunzipSync(gz).toString("utf8") !== json) {
    throw new Error("timeline gzip round trip differs");
  }
  return gz;
}

const BATCH = 200;
/** Old gzips moved per run of the periodic job; the big move is done by the script, outside the server. */
const MOVE_PER_RUN = 1000;

/** Gzips the plain time axes that are still in the table (rows from before the packing and any left over); returns how many rows were packed. */
export function compressTimelines(): number {
  const pick = sqlite.prepare(`select participant_id id, data_json raw from encounter_timelines where data_json <> '' and data_gz is null order by participant_id limit ${BATCH}`);
  const pack = sqlite.prepare("update encounter_timelines set data_gz = ?, data_json = '' where participant_id = ? and data_json = ?");
  let packed = 0;
  for (;;) {
    const rows = pick.all() as { id: number; raw: string }[];
    if (rows.length === 0) {
      return packed;
    }
    sqlite.transaction(() => {
      for (const row of rows) {
        packed += pack.run(packTimeline(row.raw), row.id, row.raw).changes;
      }
    })();
  }
}

/**
 * Gzips the payloads of settled uploads older than `olderThanHours` into the archive file and empties the plain column; returns how many rows were packed.
 * The archive is written and read back first (a commit over both files is not atomic), the main table is emptied only for what is safely there.
 */
export function compressSettledUploads(olderThanHours = 6): number {
  const pick = sqlite.prepare(
    `select id, raw_payload_json raw from uploads
     where status = 'merged' and raw_payload_json <> '' and raw_payload_gz is null and received_at < datetime('now', ?)
     order by id limit ${BATCH}`,
  );
  const put = sqlite.prepare("insert or replace into arch.payloads (upload_id, gz) values (?, ?)");
  const check = sqlite.prepare("select gz from arch.payloads where upload_id = ?");
  const empty = sqlite.prepare("update uploads set raw_payload_json = '' where id = ? and raw_payload_json = ?");
  let packed = 0;
  for (;;) {
    const rows = pick.all(`-${olderThanHours} hours`) as { id: number; raw: string }[];
    if (rows.length === 0) {
      return packed;
    }
    const gzips = rows.map((row) => {
      const gz = gzipSync(Buffer.from(row.raw, "utf8"), { level: 9 });
      if (gunzipSync(gz).toString("utf8") !== row.raw) {
        throw new Error(`upload ${row.id}: gzip round trip differs`); // nothing is emptied then
      }
      return gz;
    });
    sqlite.transaction(() => rows.forEach((row, i) => put.run(row.id, gzips[i])))();
    sqlite.transaction(() => {
      rows.forEach((row, i) => {
        const stored = check.get(row.id) as { gz: Buffer } | undefined;
        if (stored && stored.gz.equals(gzips[i])) {
          packed += empty.run(row.id, row.raw).changes;
        }
      });
    })();
  }
}

/**
 * Moves gzips that sit in the uploads table itself (rows from before the archive file existed) into the archive file; returns how many rows were moved.
 * At most `limit` rows per call, because the database calls block the server while they run; scripts/move-payloads-to-archive.ts does the big run outside the server.
 */
export function moveGzipPayloadsToArchive(limit = BATCH): number {
  const pick = sqlite.prepare("select id, raw_payload_gz gz from uploads where raw_payload_gz is not null order by id limit ?");
  const put = sqlite.prepare("insert or replace into arch.payloads (upload_id, gz) values (?, ?)");
  const check = sqlite.prepare("select gz from arch.payloads where upload_id = ?");
  const empty = sqlite.prepare("update uploads set raw_payload_gz = null where id = ? and raw_payload_gz is not null");
  const rows = pick.all(limit) as { id: number; gz: Buffer }[];
  if (rows.length === 0) {
    return 0;
  }
  sqlite.transaction(() => rows.forEach((row) => put.run(row.id, row.gz)))();
  let moved = 0;
  sqlite.transaction(() => {
    for (const row of rows) {
      const stored = check.get(row.id) as { gz: Buffer } | undefined;
      if (stored && stored.gz.equals(row.gz)) {
        moved += empty.run(row.id).changes;
      }
    }
  })();
  return moved;
}

/** Drops the payload of profile-only uploads (no fight) older than `keepDays`, wherever it is stored; returns how many. */
export function dropOldProfilePayloads(keepDays = 90): number {
  const ids = sqlite
    .prepare(
      `select u.id from uploads u
       where u.status = 'merged' and u.matched_encounter_id is null and u.received_at < datetime('now', ?)
         and (u.raw_payload_json <> '' or u.raw_payload_gz is not null or exists (select 1 from arch.payloads a where a.upload_id = u.id))`,
    )
    .all(`-${keepDays} days`) as { id: number }[];
  const dropArchived = sqlite.prepare("delete from arch.payloads where upload_id = ?");
  const dropRow = sqlite.prepare("update uploads set raw_payload_json = '', raw_payload_gz = null where id = ?");
  for (const { id } of ids) {
    dropArchived.run(id);
    dropRow.run(id);
  }
  return ids.length;
}

/** Runs both steps now and then every six hours. Off under tests. */
export function startUploadArchive(log: { info: (o: object, m: string) => void; error: (e: unknown) => void }): void {
  if (process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const run = () => {
    try {
      const packed = compressSettledUploads() + compressTimelines() + moveGzipPayloadsToArchive(MOVE_PER_RUN);
      const chained = backfillChainUsage();
      if (chained > 0) {
        log.info({ chained }, "upload archive: chain usage filled in");
      }
      const dropped = dropOldProfilePayloads(Number(process.env.UPLOAD_PROFILE_PAYLOAD_KEEP_DAYS ?? 90));
      if (packed > 0 || dropped > 0) {
        log.info({ packed, dropped }, "upload archive: payloads packed or dropped");
      }
    } catch (err) {
      log.error(err);
    }
  };
  setTimeout(run, 90_000).unref();
  setInterval(run, 6 * 3600_000).unref();
}
