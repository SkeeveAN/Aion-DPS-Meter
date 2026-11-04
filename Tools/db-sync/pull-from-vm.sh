#!/usr/bin/env bash
# Takes the nightly copy of the live database (aiondps.com) from the always-on VM "debian" in the home network into Backend/data/:
#   Backend/data/dpsmeter.sqlite              the current copy, what the local backend uses
#   Backend/data/backups/dpsmeter-YYYY-MM-DD.sqlite.gz   one per day, the newest 14 are kept
#   Backend/data/uploads-archive.sqlite       the second file with the gzipped raw payloads; the one before it as .prev
# The VM fetches both files from the server once a night (Tools/db-sync/pull-db-vm.sh, cron 3:50), so the server is asked once a day and this only
# crosses the home network. Nothing is taken when the VM has no newer copy than the one here. Both folders are git-ignored: the dump holds hashed IPs and raw
# upload payloads, and this repository is public.
set -euo pipefail
cd "$(dirname "$0")/../../Backend/data"
mkdir -p backups
day=$(date +%F)
ssh_vm() { ssh -o RemoteCommand=none -o RequestTTY=no -o BatchMode=yes -o ConnectTimeout=10 debian "$@"; }

remote_time=$(ssh_vm 'stat -c %Y "$HOME/dpsmeter-db/dpsmeter.sqlite"')
local_time=$(stat -c %Y dpsmeter.sqlite 2>/dev/null || echo 0)
if [ "$remote_time" -le "$local_time" ]; then
  echo "$(date -Is) the VM has no newer copy than this one"
  exit 0
fi

tmp=$(mktemp ./dpsmeter-pull-XXXXXX.sqlite)
tmp_archive=$(mktemp ./uploads-archive-pull-XXXXXX.sqlite)
trap 'rm -f "$tmp" "$tmp_archive"' EXIT

scp -q -o RemoteCommand=none debian:dpsmeter-db/dpsmeter.sqlite "$tmp"
[ "$(head -c 15 "$tmp")" = "SQLite format 3" ] && [ "$(stat -c %s "$tmp")" -gt 1000000 ]
gzip -c "$tmp" > "backups/dpsmeter-$day.sqlite.gz"
rm -f dpsmeter.sqlite-wal dpsmeter.sqlite-shm
touch -d "@$remote_time" "$tmp"
mv "$tmp" dpsmeter.sqlite
ls -1t backups/dpsmeter-*.sqlite.gz | tail -n +15 | xargs -r rm --
echo "$(date -Is) main $(du -h dpsmeter.sqlite | cut -f1) taken from the VM, $(ls backups | wc -l) daily copies kept"

# The archive only grows: a copy clearly smaller than the one we have is a cut off transfer and is not taken over.
scp -q -o RemoteCommand=none debian:dpsmeter-db/uploads-archive.sqlite "$tmp_archive"
[ "$(head -c 15 "$tmp_archive")" = "SQLite format 3" ]
new_size=$(stat -c %s "$tmp_archive")
old_size=$(stat -c %s uploads-archive.sqlite 2>/dev/null || echo 0)
if [ "$new_size" -lt $((old_size / 10 * 9)) ]; then
  echo "$(date -Is) archive copy is smaller than the one we have ($new_size < $old_size bytes), not taken over" >&2
  exit 1
fi
rm -f uploads-archive.sqlite-wal uploads-archive.sqlite-shm
[ -f uploads-archive.sqlite ] && mv -f uploads-archive.sqlite uploads-archive.sqlite.prev
mv "$tmp_archive" uploads-archive.sqlite
echo "$(date -Is) archive $(du -h uploads-archive.sqlite | cut -f1) taken from the VM"
