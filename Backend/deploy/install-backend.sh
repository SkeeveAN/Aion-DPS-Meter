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

# Bei jedem Deploy einmal VACUUM: der Dienst steht ohnehin, die Datei gibt freie Seiten ans Dateisystem zurueck.
# Braucht freien Platz in Hoehe der DB; fehlt er, wird uebersprungen (kein Abbruch des Deploys).
DB_FILE="$REPO_DIR/Backend/data/dpsmeter.sqlite"
if [[ -f "$DB_FILE" ]]; then
  echo "==> Stoppe Service fuer VACUUM..."
  systemctl stop "$SERVICE_NAME"
  DB_BYTES=$(stat -c %s "$DB_FILE")
  FREE_BYTES=$(df --output=avail -B1 "$(dirname "$DB_FILE")" | tail -1 | tr -d ' ')
  if (( FREE_BYTES > DB_BYTES * 2 )); then
    echo "==> VACUUM ($((DB_BYTES / 1048576)) MB)..."
    su - "$SERVICE_USER" -s /bin/bash -c "sqlite3 '$DB_FILE' 'PRAGMA wal_checkpoint(TRUNCATE); VACUUM;'" \
      || echo "WARNUNG: VACUUM fehlgeschlagen, Deploy laeuft weiter." >&2
    echo "==> Danach $(( $(stat -c %s "$DB_FILE") / 1048576 )) MB."
  else
    echo "WARNUNG: zu wenig freier Platz fuer VACUUM, uebersprungen." >&2
  fi
fi

echo "==> Starte Service neu..."
systemctl restart "$SERVICE_NAME"
sleep 2
systemctl status "$SERVICE_NAME" --no-pager -l | head -12

echo "==> Fertig."
