#!/usr/bin/env bash
# Pulls a consistent copy of the live database (aiondps.com) into Backend/data/:
#   Backend/data/dpsmeter.sqlite              the current copy, what the local backend uses
#   Backend/data/backups/dpsmeter-YYYY-MM-DD.sqlite.gz   one per day, the newest 14 are kept
#   Backend/data/uploads-archive.sqlite       the second file: the gzipped raw payloads of settled uploads (it only grows and the content is gzip already, so
#                                             one current copy and the one before it as .prev, no daily gz); fetched in the same run as the main file
# "--archive-only" fetches just the second file.
# The server makes the copy with VACUUM INTO (the database runs in WAL mode, a plain file copy could be torn; sqlite's ".backup" starts over every time the live
# backend writes in between, and on the running server it never finished: 4.11.2026 it had read 700 GB for a 1.3 GB file). The copy has no free pages. Both folders are git-ignored: the dump holds hashed IPs and raw
# upload payloads, and this repository is public.
set -euo pipefail
cd "$(dirname "$0")/../../Backend/data"
mkdir -p backups
day=$(date +%F)
remote=/tmp/dpsmeter-pull-$$.sqlite
remote_archive=/tmp/dpsmeter-pull-archive-$$.sqlite
tmp=$(mktemp ./dpsmeter-pull-XXXXXX.sqlite)
tmp_archive=$(mktemp ./uploads-archive-pull-XXXXXX.sqlite)
trap 'rm -f "$tmp" "$tmp_archive"; ssh alfahosting "rm -f $remote $remote_archive" 2>/dev/null || true' EXIT

if [ "${1:-}" != "--archive-only" ]; then
  # The server has a limit on disk operations (about 200 IOPS): every read of the 1.2 GB database competes with the live backend. So this does the
  # least possible: one .backup (read once, written once), the copy over the wire, nothing else - no integrity_check of the copy on the server (that
  # read the whole file again). The backup API reports failure by its exit code; the header is checked here.
  ssh alfahosting "ionice -c3 nice -n 19 sqlite3 /opt/dpsmeter/Backend/data/dpsmeter.sqlite \"VACUUM INTO '$remote'\""
  scp -q -C "alfahosting:$remote" "$tmp"
  [ "$(head -c 15 "$tmp")" = "SQLite format 3" ] && [ "$(stat -c %s "$tmp")" -gt 1000000 ]

  gzip -c "$tmp" > "backups/dpsmeter-$day.sqlite.gz"
  rm -f dpsmeter.sqlite-wal dpsmeter.sqlite-shm
  mv "$tmp" dpsmeter.sqlite
  ls -1t backups/dpsmeter-*.sqlite.gz | tail -n +15 | xargs -r rm --
  echo "$day: $(du -h dpsmeter.sqlite | cut -f1) pulled, $(ls backups | wc -l) daily copies kept"
fi

# The second file (see Backend/src/uploadArchive.ts). Same way as above, one .backup that is read once; no -C because the content is gzip already. A copy that is
# clearly smaller than the one we have is not taken over (the file only grows, so that would be a damaged or cut off transfer).
archive_remote_file=/opt/dpsmeter/Backend/data/uploads-archive.sqlite
if ssh alfahosting "test -f $archive_remote_file"; then
  ssh alfahosting "ionice -c3 nice -n 19 sqlite3 $archive_remote_file \"VACUUM INTO '$remote_archive'\""
  scp -q "alfahosting:$remote_archive" "$tmp_archive"
  [ "$(head -c 15 "$tmp_archive")" = "SQLite format 3" ]
  new_size=$(stat -c %s "$tmp_archive")
  old_size=$(stat -c %s uploads-archive.sqlite 2>/dev/null || echo 0)
  if [ "$new_size" -lt $((old_size / 10 * 9)) ]; then
    echo "archive copy is smaller than the one we have ($new_size < $old_size bytes), not taken over" >&2
    exit 1
  fi
  rm -f uploads-archive.sqlite-wal uploads-archive.sqlite-shm
  [ -f uploads-archive.sqlite ] && mv -f uploads-archive.sqlite uploads-archive.sqlite.prev
  mv "$tmp_archive" uploads-archive.sqlite
  echo "$day: archive $(du -h uploads-archive.sqlite | cut -f1) pulled"
fi
