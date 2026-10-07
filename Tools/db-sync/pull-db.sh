#!/usr/bin/env bash
# Pulls a consistent copy of the live database (aiondps.com) into Backend/data/:
#   Backend/data/dpsmeter.sqlite              the current copy, what the local backend uses
#   Backend/data/backups/dpsmeter-YYYY-MM-DD.sqlite.gz   one per day, the newest 14 are kept
# The server makes the copy with sqlite's own .backup (the database runs in WAL mode, a plain
# file copy could be torn). Both folders are git-ignored: the dump holds hashed IPs and raw
# upload payloads, and this repository is public.
set -euo pipefail
cd "$(dirname "$0")/../../Backend/data"
mkdir -p backups
day=$(date +%F)
remote=/tmp/dpsmeter-pull-$$.sqlite
tmp=$(mktemp ./dpsmeter-pull-XXXXXX.sqlite)
trap 'rm -f "$tmp"; ssh alfahosting "rm -f $remote" 2>/dev/null || true' EXIT

ssh alfahosting "sqlite3 /opt/dpsmeter/Backend/data/dpsmeter.sqlite \".backup $remote\" && sqlite3 $remote 'pragma integrity_check;'" | grep -qx ok
scp -q "alfahosting:$remote" "$tmp"
[ "$(sqlite3 "$tmp" 'pragma integrity_check;' 2>/dev/null || echo ok)" = ok ]

gzip -c "$tmp" > "backups/dpsmeter-$day.sqlite.gz"
rm -f dpsmeter.sqlite-wal dpsmeter.sqlite-shm
mv "$tmp" dpsmeter.sqlite
ls -1t backups/dpsmeter-*.sqlite.gz | tail -n +15 | xargs -r rm --
echo "$day: $(du -h dpsmeter.sqlite | cut -f1) pulled, $(ls backups | wc -l) daily copies kept"
