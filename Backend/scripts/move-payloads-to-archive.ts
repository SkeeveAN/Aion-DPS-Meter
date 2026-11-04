/**
 * One-off: moves the gzipped raw payloads that still sit in the uploads table into the archive file (uploads-archive.sqlite next to the database),
 * in small batches with pauses, so the live server (about 200 disk operations per second) keeps room. Safe to stop and start again: each batch is
 * written to the archive and read back before the row is emptied. The main file only gets smaller after a VACUUM (the weekly cron job does it
 * when more than a fifth of the pages are free).
 *
 * Run on the server, as the service user, next to the running backend:
 *   cd /opt/dpsmeter/Backend && ionice -c3 nice -n 19 node_modules/.bin/tsx scripts/move-payloads-to-archive.ts [pause-ms] [batch]
 */
import { sqlite } from "../src/db/client.js";
import { moveGzipPayloadsToArchive } from "../src/uploadArchive.js";

const pauseMs = Number(process.argv[2] ?? 300);
const batch = Number(process.argv[3] ?? 200);
const left = () => (sqlite.prepare("select count(*) n from uploads where raw_payload_gz is not null").get() as { n: number }).n;

let moved = 0;
const total = left();
console.log(`${total} gzipped payloads to move (batch ${batch}, pause ${pauseMs} ms)`);
const started = Date.now();
for (;;) {
  const n = moveGzipPayloadsToArchive(batch);
  if (n === 0) {
    break;
  }
  moved += n;
  if (Math.floor(moved / batch) % 25 === 0) {
    const seconds = Math.round((Date.now() - started) / 1000);
    console.log(`${moved} of ${total} moved after ${seconds} s`);
  }
  await new Promise((resolve) => setTimeout(resolve, pauseMs));
}
const archived = (sqlite.prepare("select count(*) n from arch.payloads").get() as { n: number }).n;
console.log(`done: ${moved} moved, ${left()} left in the table, ${archived} in the archive file`);
sqlite.pragma("arch.wal_checkpoint(TRUNCATE)");
sqlite.close();
