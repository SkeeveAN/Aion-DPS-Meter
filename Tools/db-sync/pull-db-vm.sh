#!/usr/bin/env bash
# Nightly copy of the live database (aiondps.com) on the always-on VM "debian" in the home network (cron 3:50, see below).
# It runs there instead of on the PC, so the server is asked once a day, and the PC takes its copy from the VM (Tools/db-sync/pull-from-vm.sh).
#
#   ~/dpsmeter-db/dpsmeter.sqlite                          the current copy of the main file
#   ~/dpsmeter-db/backups/dpsmeter-YYYY-MM-DD.sqlite.gz    one per day, the newest KEEP_DAYS are kept (the VM has 24 GB: 7 copies fit)
#   ~/dpsmeter-db/uploads-archive.sqlite                   the second file with the gzipped raw payloads; the one before it as .prev
#
# The key ~/.ssh/dpsmeter_pull may only run /usr/local/sbin/dpsmeter-dbpull on the server (forced command, from= the home IP): the request word
# "main" or "archive" makes the server stream a consistent sqlite .backup of that file to stdout. The server has about 200 disk operations per
# second, so nothing else is read there (no integrity check on its side; it is done here on the copy).
#
# crontab of the user skeeve on the VM:
#   50 3 * * * flock -n /tmp/pull-db.lock $HOME/dpsmeter-db/pull-db.sh >> $HOME/dpsmeter-db/pull-db.log 2>&1
set -euo pipefail
KEEP_DAYS="${KEEP_DAYS:-7}"
MIN_FREE_MB="${MIN_FREE_MB:-5000}"
cd "$(dirname "$0")"
mkdir -p backups
day=$(date +%F)
server=root@89.22.107.203
ssh_pull() { ssh -4 -C -i "$HOME/.ssh/dpsmeter_pull" -o IdentitiesOnly=yes -o BatchMode=yes -o ConnectTimeout=20 -o ServerAliveInterval=30 "$server" "$1"; }

free_mb=$(df --output=avail -m . | tail -1 | tr -d ' ')
if [ "$free_mb" -lt "$MIN_FREE_MB" ]; then
  echo "$(date -Is) only ${free_mb} MB free, less than ${MIN_FREE_MB}: not pulling" >&2
  exit 1
fi

tmp=$(mktemp ./pull-XXXXXX.sqlite)
tmp_archive=$(mktemp ./pull-archive-XXXXXX.sqlite)
trap 'rm -f "$tmp" "$tmp_archive"' EXIT

ssh_pull main > "$tmp"
[ "$(head -c 15 "$tmp")" = "SQLite format 3" ] && [ "$(stat -c %s "$tmp")" -gt 1000000 ]
[ "$(sqlite3 "$tmp" 'pragma quick_check')" = "ok" ]
gzip -c "$tmp" > "backups/dpsmeter-$day.sqlite.gz"
mv "$tmp" dpsmeter.sqlite
ls -1t backups/dpsmeter-*.sqlite.gz | tail -n +$((KEEP_DAYS + 1)) | xargs -r rm --
echo "$(date -Is) main $(du -h dpsmeter.sqlite | cut -f1) pulled, $(ls backups | wc -l) daily copies kept"

# The archive only grows: a copy clearly smaller than the one we have is a cut off transfer and is not taken over.
ssh_pull archive > "$tmp_archive"
[ "$(head -c 15 "$tmp_archive")" = "SQLite format 3" ]
[ "$(sqlite3 "$tmp_archive" 'pragma quick_check')" = "ok" ]
new_size=$(stat -c %s "$tmp_archive")
old_size=$(stat -c %s uploads-archive.sqlite 2>/dev/null || echo 0)
if [ "$new_size" -lt $((old_size / 10 * 9)) ]; then
  echo "$(date -Is) archive copy is smaller than the one we have ($new_size < $old_size bytes), not taken over" >&2
  exit 1
fi
[ -f uploads-archive.sqlite ] && mv -f uploads-archive.sqlite uploads-archive.sqlite.prev
mv "$tmp_archive" uploads-archive.sqlite
echo "$(date -Is) archive $(du -h uploads-archive.sqlite | cut -f1) pulled"
