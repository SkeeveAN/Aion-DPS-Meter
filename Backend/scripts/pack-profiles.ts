/**
 * One-off: gzips the profile columns that still hold plain JSON text (player_profiles: skills, daevanion, gear; profile_setups: data), in small
 * batches with pauses so the live server (about 200 disk operations per second) keeps room. Safe to stop and start again (it only touches rows that are
 * still plain text), and every row is read back and compared with its original before the batch is committed. The file only gets smaller after a VACUUM
 * (the weekly cron job does it when more than a fifth of the pages are free).
 *
 * Run on the server, as the service user, next to the running backend (a backend with the packed-column code must be running first, else it could not read the rows):
 *   cd /opt/dpsmeter/Backend && ionice -c3 nice -n 19 node_modules/.bin/tsx scripts/pack-profiles.ts [pause-ms] [batch]
 * The way back (before it, or after a rollback of the backend code): scripts/pack-profiles.ts --unpack [pause-ms] [batch]
 * Dry run (counts only): scripts/pack-profiles.ts --count
 */
import { sqlite } from "../src/db/client.js";
import { PACKED_COLUMNS, packBatch, plainLeft, unpackBatch } from "../src/packProfiles.js";

const args = process.argv.slice(2);
const unpack = args.includes("--unpack");
const countOnly = args.includes("--count");
const numbers = args.filter((a) => !a.startsWith("--")).map(Number);
const pauseMs = numbers[0] ?? 400;
const batch = numbers[1] ?? 500;

if (countOnly) {
  for (const c of PACKED_COLUMNS) {
    console.log(`${c.table}.${c.column}: ${plainLeft(sqlite, c)} plain rows`);
  }
  sqlite.close();
  process.exit(0);
}

console.log(`${unpack ? "unpacking" : "packing"} (batch ${batch}, pause ${pauseMs} ms)`);
const started = Date.now();
for (const c of PACKED_COLUMNS) {
  let after = 0;
  let done = 0;
  const total = unpack ? undefined : plainLeft(sqlite, c);
  for (;;) {
    const r = unpack ? unpackBatch(sqlite, c, after, batch) : packBatch(sqlite, c, after, batch);
    if (r.last === null) {
      break;
    }
    after = r.last;
    done += "packed" in r ? r.packed : r.unpacked;
    if (Math.floor(done / batch) % 20 === 0) {
      console.log(`${c.table}.${c.column}: ${done}${total === undefined ? "" : ` of ${total}`} after ${Math.round((Date.now() - started) / 1000)} s`);
    }
    await new Promise((resolve) => setTimeout(resolve, pauseMs));
  }
  console.log(`${c.table}.${c.column}: done, ${done} ${unpack ? "unpacked" : "packed"}`);
}
sqlite.pragma("wal_checkpoint(PASSIVE)");
sqlite.close();
