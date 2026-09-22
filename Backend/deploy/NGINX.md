# nginx-Checkliste für aiondps.com

Die nginx-Konfiguration liegt **nicht** im Repo, sondern direkt auf dem Server (`alfahosting`):
`aiondps.com:443` proxied auf das Backend unter `127.0.0.1:4000`. Die folgenden Punkte
gehören dort hin, damit die SEO-Arbeit im Backend (echte Pfad-URLs, Shells, Sitemap) auch bei
Google ankommt. Nach jeder Änderung mit `nginx -t` prüfen und mit `curl -I` nachweisen.

## Redirects / Canonical Host

- Kanonischer Host ist `https://aiondps.com` (ohne `www`). `http://` → `https://` und
  `www.aiondps.com` → `aiondps.com` jeweils per **301**.
- HSTS: `add_header Strict-Transport-Security "max-age=31536000" always;`
- Prüfen: `curl -I http://aiondps.com/`, `curl -I https://www.aiondps.com/` → jeweils `301`
  mit `Location: https://aiondps.com/...`.

## Proxy

- `proxy_set_header Host $host;`, `X-Forwarded-Proto $scheme`, `X-Forwarded-For` - das Backend
  liest davon nichts für Canonical-URLs (die kommen aus `BASE_URL`), aber Logs und Rate-Limit
  brauchen die echte Client-IP.
- `proxy_intercept_errors off;` - das Backend liefert bei unbekannten Pfaden selbst eine 404-Seite
  mit `noindex`; nginx darf die nicht durch eine eigene Fehlerseite ersetzen.

## Kompression / Caching

- `gzip on; gzip_types text/html text/css application/javascript application/json application/xml;`
- Cache-Header setzt das Backend selbst (`no-cache` für HTML-Shells, `max-age=3600` + ETag für
  Assets, `max-age=600` für `sitemap.xml`) - nichts überschreiben.

## Backend-`.env` auf dem Server

- `BASE_URL=https://aiondps.com` (Default im Code, explizit setzen schadet nicht).
- `CORS_ORIGIN=https://aiondps.com` statt `*` - der Desktop-Client ist kein Browser und braucht
  kein CORS; das Web-Frontend ist same-origin.

## Nach dem Deploy

1. `curl -s https://aiondps.com/robots.txt` und `curl -s https://aiondps.com/sitemap.xml | head`
2. Google Search Console + Bing Webmaster Tools per **DNS-TXT** verifizieren (überlebt jede
   Shell-Änderung) und `https://aiondps.com/sitemap.xml` einreichen.
3. Rich-Results-Test auf `/download` und einer Boss-Seite; Lighthouse (SEO ≥ 95).
4. Einen Boss-Link in einem Discord-Testkanal posten - Titel/Beschreibung/Bild müssen der Seite
   entsprechen (`og:*`-Tags aus `src/seo/meta.ts`).
