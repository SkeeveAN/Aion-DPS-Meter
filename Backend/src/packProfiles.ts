import type Database from "better-sqlite3";
import { PACK_MIN_LENGTH, packJson, unpackJson } from "./db/packedJson.js";

/** The columns that are stored gzipped (see db/packedJson.ts): table, key column, column. */
export const PACKED_COLUMNS: { table: string; key: string; column: string }[] = [
  { table: "player_profiles", key: "player_id", column: "skills_json" },
  { table: "player_profiles", key: "player_id", column: "daevanion_json" },
  { table: "player_profiles", key: "player_id", column: "gear_json" },
  { table: "profile_setups", key: "id", column: "data_json" },
];

/** How many rows of a column are still plain text and long enough to be packed. */
export function plainLeft(sqlite: Database.Database, c: { table: string; column: string }): number {
  return (sqlite.prepare(`select count(*) n from ${c.table} where typeof(${c.column}) = 'text' and length(${c.column}) >= ?`).get(PACK_MIN_LENGTH) as { n: number }).n;
}

/**
 * Packs up to `batch` plain rows of one column after key `after` (ascending); returns how many it packed and the last key it looked at (null = nothing left).
 * Each row is read back after the update and compared with the original text, the update only applies while the row still holds that text (a live
 * write between the read and the update wins), and the whole batch is one transaction.
 */
export function packBatch(sqlite: Database.Database, c: { table: string; key: string; column: string }, after: number, batch: number): { packed: number; last: number | null } {
  const pick = sqlite.prepare(
    `select ${c.key} k, ${c.column} v from ${c.table} where ${c.key} > ? and typeof(${c.column}) = 'text' and length(${c.column}) >= ${PACK_MIN_LENGTH} order by ${c.key} limit ?`,
  );
  const rows = pick.all(after, batch) as { k: number; v: string }[];
  if (rows.length === 0) {
    return { packed: 0, last: null };
  }
  const update = sqlite.prepare(`update ${c.table} set ${c.column} = ? where ${c.key} = ? and ${c.column} = ?`);
  const check = sqlite.prepare(`select ${c.column} v from ${c.table} where ${c.key} = ?`);
  let packed = 0;
  sqlite.transaction(() => {
    for (const row of rows) {
      const value = packJson(row.v);
      if (typeof value === "string") {
        continue;
      }
      if (update.run(value, row.k, row.v).changes === 1) {
        const back = (check.get(row.k) as { v: Buffer | string }).v;
        if (unpackJson(back) !== row.v) {
          throw new Error(`${c.table}.${c.column} ${row.k}: the packed value differs from the original`);
        }
        packed++;
      }
    }
  })();
  return { packed, last: rows[rows.length - 1].k };
}

/** The opposite of packBatch, for the way back: unpacks every gzip BLOB of a column into plain text (batch of rows after key `after`). */
export function unpackBatch(sqlite: Database.Database, c: { table: string; key: string; column: string }, after: number, batch: number): { unpacked: number; last: number | null } {
  const rows = sqlite.prepare(`select ${c.key} k, ${c.column} v from ${c.table} where ${c.key} > ? and typeof(${c.column}) = 'blob' order by ${c.key} limit ?`).all(after, batch) as { k: number; v: Buffer }[];
  if (rows.length === 0) {
    return { unpacked: 0, last: null };
  }
  const update = sqlite.prepare(`update ${c.table} set ${c.column} = ? where ${c.key} = ? and typeof(${c.column}) = 'blob'`);
  let unpacked = 0;
  sqlite.transaction(() => {
    for (const row of rows) {
      unpacked += update.run(unpackJson(row.v), row.k).changes;
    }
  })();
  return { unpacked, last: rows[rows.length - 1].k };
}
