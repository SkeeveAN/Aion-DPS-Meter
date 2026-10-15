#!/usr/bin/env python3
"""Where do human visitors come from, apart from search engines? Reads the nginx access log.

Usage: referrers.py <access.log> > herkunft.html
Bots (same detection as bots.py), /metrics, own-site referrers and asset/API requests are left out.
"""
import html
import re
import sys
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from urllib.parse import urlsplit

from bots import LINE, bot_name, mask

SEARCH = re.compile(
    r"(^|\.)(google\.[a-z.]+|bing\.com|duckduckgo\.com|brave\.com|yahoo\.[a-z.]+|ecosia\.org|yandex\.[a-z.]+|baidu\.com"
    r"|startpage\.com|qwant\.com|kagi\.com|ask\.com|aol\.com|naver\.com|seznam\.cz|sogou\.com|mojeek\.com|presearch\.com"
    r"|search\.[a-z.]+|googlequicksearchbox)$", re.I)
OWN = {"aiondps.com", "www.aiondps.com"}
STATIC = re.compile(r"\.(js|css|png|jpe?g|webp|gif|svg|ico|woff2?|json|txt|xml|map)$|^/(api|og|images|assets|locales|ws)/", re.I)


def host_of(ref):
    try:
        h = urlsplit(ref).hostname or ref
    except ValueError:
        return None
    return h.lower()


def main(path):
    now = datetime.now().astimezone()
    hits, search = [], Counter()
    embeds = defaultdict(lambda: {"n": 0, "bad": 0, "files": Counter()})
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = LINE.match(line)
            if not m:
                continue
            ip, ts, req, status, ref, ua = m.groups()
            parts = req.split(" ")
            url = parts[1].split("?")[0] if len(parts) > 1 else ""
            if ref in ("", "-") or bot_name(ua) or url == "/metrics":
                continue
            h = host_of(ref)
            if STATIC.search(url):
                if h and h not in OWN and not SEARCH.search(h):
                    g = embeds[h]
                    g["n"] += 1
                    g["bad"] += int(status) >= 400
                    g["files"][url] += 1
                continue
            if int(status) >= 400:
                continue
            if not h or h in OWN:
                continue
            if SEARCH.search(h) and not h.startswith(("gemini.", "chat.", "copilot.")):
                search[h] += 1
                continue
            r = urlsplit(ref)
            hits.append((datetime.strptime(ts, "%d/%b/%Y:%H:%M:%S %z"), ip, h, f"{r.scheme}://{h}{r.path}".rstrip("/") or ref, url))

    by = defaultdict(lambda: {"n": 0, "ips": set(), "first": None, "last": None, "land": Counter(), "refs": Counter(), "days": Counter()})
    for t, ip, h, ref, url in hits:
        g = by[h]
        g["n"] += 1
        g["ips"].add(ip)
        g["first"] = min(g["first"] or t, t)
        g["last"] = max(g["last"] or t, t)
        g["land"][url] += 1
        g["refs"][ref] += 1
        g["days"][t.strftime("%Y-%m-%d")] += 1
    days = [(now - timedelta(days=i)).strftime("%Y-%m-%d") for i in range(13, -1, -1)]
    fmt = lambda t: t.strftime("%d.%m. %H:%M")
    e = html.escape

    out = [f"""<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex,nofollow"><title>Herkunft – aiondps.com</title><style>
:root{{--bg:#14161b;--fg:#e6e8ee;--dim:#8b92a3;--card:#1c1f27;--line:#2b303b}}
@media(prefers-color-scheme:light){{:root{{--bg:#f5f6f8;--fg:#1b1e25;--dim:#606878;--card:#fff;--line:#dde0e7}}}}
body{{margin:0;padding:24px 16px;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif}}
main{{max-width:1872px;margin:auto}}a{{color:inherit}}h1{{font-size:20px}}h2{{font-size:16px;margin:28px 0 8px}}
.wrap{{overflow-x:auto;background:var(--card);border:1px solid var(--line);border-radius:10px}}
table{{border-collapse:collapse;width:100%}}th,td{{padding:6px 10px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top;white-space:nowrap}}
th{{color:var(--dim);font-weight:600;font-size:12px}}td.n,th.n{{text-align:right;font-variant-numeric:tabular-nums}}
td.p{{white-space:normal;max-width:520px;color:var(--dim);font-size:12px;word-break:break-all}}p.s{{color:var(--dim)}}</style></head><body><main>
<h1>Herkunft der Besucher (ohne Suchmaschinen)</h1>
<p class="s"><a href="./">← Übersicht</a> · Stand {e(now.strftime('%d.%m.%Y %H:%M'))} · nur Menschen, nur Seitenaufrufe (keine Bilder/API), eigene Seite und Bots ausgeblendet ·
Suchmaschinen zusammen: <b>{sum(search.values())}</b> Aufrufe ({e(', '.join(f'{h} {n}' for h, n in search.most_common(6)) or '–')}) · Query-Strings der Quell-URLs sind abgeschnitten.</p>
<h2>Quellen</h2><div class="wrap"><table><tr><th>Quelle<th class="n">Aufrufe<th class="n">Besucher (IPs)<th>Erster<th>Letzter<th>Quell-Seiten<th>Landeseiten</tr>"""]
    for h, g in sorted(by.items(), key=lambda kv: -kv[1]["n"]):
        refs = " · ".join(f"{e(r)} ({n})" for r, n in g["refs"].most_common(3))
        land = ", ".join(f"{e(u)} ({n})" for u, n in g["land"].most_common(3))
        out.append(f'<tr><td>{e(h)}<td class="n">{g["n"]}<td class="n">{len(g["ips"])}<td>{fmt(g["first"])}<td>{fmt(g["last"])}<td class="p">{refs}<td class="p">{land}</tr>')
    out.append('</table></div><h2>Aufrufe pro Tag (die zehn stärksten Quellen)</h2><div class="wrap"><table><tr><th>Quelle'
               + "".join(f'<th class="n">{d[5:]}' for d in days) + "</tr>")
    for h, g in sorted(by.items(), key=lambda kv: -kv[1]["n"])[:10]:
        out.append(f"<tr><td>{e(h)}" + "".join(f'<td class="n">{g["days"][d] or "·"}' for d in days) + "</tr>")
    out.append('</table></div><h2>Eingebundene Dateien (fremde Seiten laden Bilder/Skripte von uns)</h2><div class="wrap"><table>'
               '<tr><th>Seite<th class="n">Abrufe<th class="n">davon Fehler<th>Meistgeholt</tr>')
    for h, g in sorted(embeds.items(), key=lambda kv: -kv[1]["n"])[:25]:
        out.append(f'<tr><td>{e(h)}<td class="n">{g["n"]}<td class="n">{g["bad"]}<td class="p">'
                   + ", ".join(f"{e(u)} ({n})" for u, n in g["files"].most_common(4)) + "</tr>")
    out.append('</table></div><h2>Letzte 60 Besuche</h2><div class="wrap"><table><tr><th>Zeit<th>Besucher<th>Von<th>Landeseite</tr>')
    for t, ip, h, ref, url in sorted(hits, key=lambda x: x[0], reverse=True)[:60]:
        out.append(f'<tr><td>{fmt(t)}<td>{e(mask(ip))}<td class="p">{e(ref)}<td>{e(url)}</tr>')
    out.append("</table></div></main></body></html>")
    sys.stdout.write("\n".join(out))


if __name__ == "__main__":
    main(sys.argv[1])
