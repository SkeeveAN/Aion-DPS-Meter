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
systemctl daemon-reload
systemctl enable "$SERVICE_NAME" >/dev/null

# VACUUM laeuft nicht mehr im Deploy (hielt die Seite ~8 Minuten offline), sondern nachts per Cron.
install -m 0755 "$REPO_DIR/Backend/deploy/dpsmeter-vacuum" /usr/local/sbin/dpsmeter-vacuum
echo '40 4 * * * root /usr/local/sbin/dpsmeter-vacuum >> /var/log/dpsmeter-vacuum.log 2>&1' > /etc/cron.d/dpsmeter-vacuum

install -m 0755 "$REPO_DIR/Backend/deploy/dpsmeter_keys" /usr/local/bin/dpsmeter_keys

echo "==> Starte Service neu..."
systemctl restart "$SERVICE_NAME"
sleep 2
systemctl status "$SERVICE_NAME" --no-pager -l | head -12

# nginx keeps public pages for 60 s (see the aiondps cache); after a deploy they should show the new state at once.
if [[ -d /var/cache/nginx/aiondps ]]; then
  find /var/cache/nginx/aiondps -mindepth 1 -delete
fi

echo "==> Fertig."
