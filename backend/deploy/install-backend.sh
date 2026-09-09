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
  cd '$REPO_DIR/backend'
  corepack enable >/dev/null 2>&1 || true
  pnpm install
  if [[ ! -f .env ]]; then
    cp .env.example .env
  fi
  pnpm run db:migrate
  pnpm run db:seed
"

echo "==> Installiere/aktualisiere systemd-Unit..."
cp "$REPO_DIR/backend/deploy/aion-dpsmeter-backend.service" "/etc/systemd/system/$SERVICE_NAME.service"
systemctl daemon-reload
systemctl enable "$SERVICE_NAME" >/dev/null

echo "==> Starte Service neu..."
systemctl restart "$SERVICE_NAME"
sleep 2
systemctl status "$SERVICE_NAME" --no-pager -l | head -12

echo "==> Fertig."
