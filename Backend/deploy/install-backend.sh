#!/usr/bin/env bash
# Installiert die Backend-Abhängigkeiten, wendet ausstehende DB-Migrationen an
# und startet den systemd-Service (neu). Beliebig oft wiederholbar - jeder
# Schritt ist idempotent.
#
# Wird ausschließlich vom Bootstrap-Skript `dpsmeter_install` aufgerufen,
# NACHDEM der Git-Checkout bereits aktualisiert wurde - dieses Skript rührt
# absichtlich nicht selbst an git, damit es sich nicht während der eigenen
# Ausführung unter sich selbst verändert (siehe dpsmeter_install).
set -euo pipefail

REPO_DIR="/opt/dpsmeter"
SERVICE_NAME="aion-dpsmeter-backend"
SERVICE_USER="aion-dpsmeter"

if [[ "$EUID" -ne 0 ]]; then
  echo "install-backend.sh muss als root laufen (braucht systemctl + die Service-Unit-Datei)." >&2
  exit 1
fi

echo "==> Installiere Backend-Abhängigkeiten..."
su - "$SERVICE_USER" -s /bin/bash -c "
  set -e
  cd '$REPO_DIR/Backend'
  corepack enable >/dev/null 2>&1 || true
  pnpm install
  if [[ ! -f .env ]]; then
    cp .env.example .env
  fi
  pnpm run db:migrate
  pnpm run db:seed
  pnpm run content:sync
"

echo "==> Installiere/aktualisiere systemd-Unit..."
cp "$REPO_DIR/Backend/deploy/aion-dpsmeter-backend.service" "/etc/systemd/system/$SERVICE_NAME.service"
# Reads the database file once in big blocks before the start (a cold start in 4 KB pieces took a quarter of an hour at 200 IOPS).
mkdir -p "/etc/systemd/system/$SERVICE_NAME.service.d"
install -m 0644 "$REPO_DIR/Backend/deploy/aion-dpsmeter-backend.prewarm.conf" "/etc/systemd/system/$SERVICE_NAME.service.d/prewarm.conf"
systemctl daemon-reload
systemctl enable "$SERVICE_NAME" >/dev/null

# VACUUM laeuft nicht im Deploy (hielt die Seite ~8 Minuten offline), sondern sonntags nachts per Cron, und das Skript tut nur etwas bei vielen freien Seiten.
install -m 0755 "$REPO_DIR/Backend/deploy/dpsmeter-vacuum" /usr/local/sbin/dpsmeter-vacuum
echo '40 4 * * 0 root /usr/local/sbin/dpsmeter-vacuum >> /var/log/dpsmeter-vacuum.log 2>&1' > /etc/cron.d/dpsmeter-vacuum

install -m 0755 "$REPO_DIR/Backend/deploy/dpsmeter_keys" /usr/local/bin/dpsmeter_keys

# Size limit of the journal (the disk operations of this server are scarce); journald is only restarted when the file changed.
mkdir -p /etc/systemd/journald.conf.d
if ! cmp -s "$REPO_DIR/Backend/deploy/journald-aiondps.conf" /etc/systemd/journald.conf.d/aiondps.conf; then
  install -m 0644 "$REPO_DIR/Backend/deploy/journald-aiondps.conf" /etc/systemd/journald.conf.d/aiondps.conf
  systemctl restart systemd-journald || true
fi

echo "==> Starte Service neu..."
systemctl restart "$SERVICE_NAME"
sleep 2
systemctl status "$SERVICE_NAME" --no-pager -l | head -12

# nginx keeps public pages for 60 s (see the aiondps cache); after a deploy they should show the new state at once.
if [[ -d /var/cache/nginx/aiondps ]]; then
  find /var/cache/nginx/aiondps -mindepth 1 -delete
fi

echo "==> Fertig."
