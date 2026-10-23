import { gunzipSync, gzipSync } from "node:zlib";
import { sql } from "drizzle-orm";
import { db, sqlite } from "./db/client.js";
import { uploads } from "./db/schema.js";

/**
 * The raw payload of an upload is only an audit copy once the upload is settled (merged): the facts are in the encounter, player and profile tables.
 * Uploads that wait for a review or were turned away keep the plain text (the review parses it). Every few hours:
 *  - a merged upload older than six hours gets its payload gzipped (checked by unpacking it again) and the plain column emptied;
 *  - the time axes of the participants (encounter_timelines) are gzipped the same way; nothing is lost, the text comes back unchanged;
 *  - the payload of a merged upload without a fight (a profile-only upload) is dropped for good after UPLOAD_PROFILE_PAYLOAD_KEEP_DAYS (default 90).
 * The upload row itself stays, so the counts of the statistics do not change.
 */

/** The raw text of an upload row, whether it is stored plain or as gzip; null when it was dropped. */
export function rawPayloadOf(row: { rawPayloadJson: string; rawPayloadGz: Buffer | null }): string | null {
  if (row.rawPayloadJson !== "") {
    return row.rawPayloadJson;
  }
  return row.rawPayloadGz ? gunzipSync(row.rawPayloadGz).toString("utf8") : null;
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

/** Gzips the payloads of settled uploads older than `olderThanHours`; returns how many rows were packed. */
export function compressSettledUploads(olderThanHours = 6): number {
  const pick = sqlite.prepare(
    `select id, raw_payload_json raw from uploads
     where status = 'merged' and raw_payload_json <> '' and raw_payload_gz is null and received_at < datetime('now', ?)
     order by id limit ${BATCH}`,
  );
  const pack = sqlite.prepare("update uploads set raw_payload_gz = ?, raw_payload_json = '' where id = ? and raw_payload_json = ?");
  let packed = 0;
  for (;;) {
    const rows = pick.all(`-${olderThanHours} hours`) as { id: number; raw: string }[];
    if (rows.length === 0) {
      return packed;
    }
    sqlite.transaction(() => {
      for (const row of rows) {
        const gz = gzipSync(Buffer.from(row.raw, "utf8"), { level: 9 });
        if (gunzipSync(gz).toString("utf8") !== row.raw) {
          throw new Error(`upload ${row.id}: gzip round trip differs`); // nothing is emptied then
        }
        packed += pack.run(gz, row.id, row.raw).changes;
      }
    })();
  }
}

/** Drops the payload of profile-only uploads (no fight) older than `keepDays`; returns how many. */
export function dropOldProfilePayloads(keepDays = 90): number {
  return db
    .update(uploads)
    .set({ rawPayloadJson: "", rawPayloadGz: null })
    .where(sql`${uploads.status} = 'merged' and ${uploads.matchedEncounterId} is null and ${uploads.receivedAt} < datetime('now', ${`-${keepDays} days`}) and (${uploads.rawPayloadJson} <> '' or ${uploads.rawPayloadGz} is not null)`)
    .run().changes;
}

/** Runs both steps now and then every six hours. Off under tests. */
export function startUploadArchive(log: { info: (o: object, m: string) => void; error: (e: unknown) => void }): void {
  if (process.env.NODE_TEST_CONTEXT) {
    return;
  }
  const run = () => {
    try {
      const packed = compressSettledUploads() + compressTimelines();
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
