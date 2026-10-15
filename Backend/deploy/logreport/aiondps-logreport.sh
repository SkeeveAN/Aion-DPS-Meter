#!/bin/bash
# Daily access-log report for aiondps.com: GoAccess for visitors (bots excluded), bots.py for crawlers.
# Served by nginx from /var/www/aiondps-logreport under a secret URL (see README.md). Runs via cron.
set -euo pipefail

LOG=/var/log/nginx/aiondps.com/https_access.log
OUT=/var/www/aiondps-logreport
HERE=$(dirname "$(readlink -f "$0")")
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT"

# Our own monitoring scrapes /metrics constantly; it would drown everything else.
grep -v '"GET /metrics' "$LOG" > "$WORK/log"

goaccess "$WORK/log" --no-progress \
  --log-format='%h %^[%d:%t %^] "%r" %s %b "%R" "%u" rt=%T urt=%^' \
  --date-format='%d/%b/%Y' --time-format='%T' \
  --ignore-crawlers --anonymize-ip --no-query-string --ignore-referrer=aiondps.com --enable-panel=REFERRERS \
  --html-prefs='{"theme":"darkBlue","layout":"horizontal","perPage":7}' --html-custom-css="$HERE/goaccess.css" --html-custom-js="$HERE/goaccess.js" \
  --html-report-title='aiondps.com – Besucher' -o "$WORK/besucher.html"

python3 "$HERE/bots.py" "$WORK/log" > "$WORK/bots.html"
python3 "$HERE/referrers.py" "$WORK/log" > "$WORK/herkunft.html"

NOW=$(date '+%d.%m.%Y %H:%M')
cat > "$WORK/index.html" <<EOF
<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex,nofollow"><title>Log-Bericht – aiondps.com</title><style>
:root{--bg:#14161b;--fg:#e6e8ee;--dim:#8b92a3;--card:#1c1f27;--line:#2b303b}
@media(prefers-color-scheme:light){:root{--bg:#f5f6f8;--fg:#1b1e25;--dim:#606878;--card:#fff;--line:#dde0e7}}
body{margin:0;padding:48px 16px;background:var(--bg);color:var(--fg);font:16px/1.5 system-ui,sans-serif}
main{max-width:560px;margin:auto}a.c{display:block;padding:16px 20px;margin:12px 0;background:var(--card);border:1px solid var(--line);border-radius:10px;color:inherit;text-decoration:none}
a.c small{display:block;color:var(--dim)}p{color:var(--dim)}</style></head><body><main>
<h1>Log-Bericht aiondps.com</h1><p>Stand $NOW · täglich neu erzeugt</p>
<a class="c" href="besucher.html">Besucher<small>Seiten, Herkunft, Browser, Fehler, Antwortzeiten – ohne Bots, IPs gekürzt</small></a>
<a class="c" href="herkunft.html">Herkunft<small>Woher kommen Besucher, die nicht von Suchmaschinen kommen</small></a>
<a class="c" href="bots.html">Bots<small>Wer kommt wie oft vorbei, echt oder gefälscht</small></a>
</main></body></html>
EOF

# GoAccess only links --html-custom-css/-js by path; inline them (the CSP allows inline only)
python3 - "$WORK/besucher.html" "$HERE/goaccess.css" "$HERE/goaccess.js" <<'PY'
import re, sys
page, css, js = sys.argv[1:]
t = open(page, encoding="utf-8").read()
t = re.sub(r"<link rel='stylesheet' href='[^']*goaccess\.css'>", lambda m: "<style>" + open(css).read() + "</style>", t, count=1)
t = re.sub(r"<script src='[^']*goaccess\.js'></script>", lambda m: "<script>" + open(js).read() + "</script>", t, count=1)
open(page, "w", encoding="utf-8").write(t)
PY

# The CSP only allows data: images, so the site favicon is embedded into every page
ICON="<link rel=\"icon\" type=\"image/png\" href=\"data:image/png;base64,$(base64 -w0 "$HERE/favicon.png")\">"
sed -i "s|<link rel=.icon. href=.data:image/x-icon[^>]*>||" "$WORK/besucher.html"   # GoAccess brings its own logo
for f in index besucher bots herkunft; do sed -i "0,/<\/head>/s||$ICON</head>|" "$WORK/$f.html"; done

# Replace atomically so a half-written file is never served
for f in index besucher bots herkunft; do mv "$WORK/$f.html" "$OUT/$f.html"; done
chmod 644 "$OUT"/*.html
