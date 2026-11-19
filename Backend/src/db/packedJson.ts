import { gunzipSync, gzipSync } from "node:zlib";
import { customType } from "drizzle-orm/sqlite-core";

/**
 * JSON columns that may hold their text gzipped (the profile lists and the setups, which made up 1.2 GB of the 2 GB database).
 *
 * The column stays TEXT (no schema change): SQLite stores whatever type a value has, so a row holds either the plain JSON text (every row written
 * before the switch, and every short value) or a gzip BLOB. `unpackJson` reads both, so old and new rows live side by side while `scripts/pack-profiles.ts`
 * works through the old ones in small batches. Values under PACK_MIN_LENGTH stay plain ("[]" and "{}" are by far the most frequent ones, and a
 * SQL comparison with '[]' keeps working for them).
 */
export const PACK_MIN_LENGTH = 256;

/** The gzip magic bytes: a plain JSON text never starts with them. */
const isGzip = (b: Buffer) => b.length > 2 && b[0] === 0x1f && b[1] === 0x8b;

/** What goes into the column: the text itself while it is short, else its gzip (checked by unpacking it again). */
export function packJson(text: string): string | Buffer {
  if (text.length < PACK_MIN_LENGTH) {
    return text;
  }
  const gz = gzipSync(Buffer.from(text, "utf8"), { level: 9 });
  if (gunzipSync(gz).toString("utf8") !== text) {
    throw new Error("json gzip round trip differs");
  }
  return gz;
}

/** The text of a column value: the plain text as it is, a gzip BLOB unpacked. */
export function unpackJson(value: string | Buffer | Uint8Array | null | undefined): string {
  if (value === null || value === undefined) {
    return "";
  }
  if (typeof value === "string") {
    return value;
  }
  const buf = Buffer.isBuffer(value) ? value : Buffer.from(value);
  return isGzip(buf) ? gunzipSync(buf).toString("utf8") : buf.toString("utf8");
}

/** A TEXT column whose value is read as text however it is stored, and written gzipped when it is long (see above). */
export const packedJson = customType<{ data: string; driverData: string | Buffer }>({
  dataType: () => "text",
  toDriver: (value) => packJson(value),
  fromDriver: (value) => unpackJson(value),
});
