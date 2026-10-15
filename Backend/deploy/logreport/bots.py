#!/usr/bin/env python3
"""Bot overview for aiondps.com from the nginx access log: who comes by, how often, and is it genuine.

Usage: bots.py <access.log> > bots.html
Google/Bing claims are verified by reverse DNS plus forward confirmation (the documented method);
everything else is listed by its self-declared user agent only.
"""
import html
import ipaddress
import json
import os
import re
import socket
import sys
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta

LINE = re.compile(r'^(\S+) \S+ \S+ \[([^\]]+)\] "([^"]*)" (\d{3}) \S+ "([^"]*)" "([^"]*)"')
NAMED = re.compile(r"([A-Za-z0-9_.\-]*(?:bot|crawler|spider|slurp|inspect|siphon|scanner)[A-Za-z0-9_.\-]*)", re.I)
TOOLS = re.compile(
    r"(curl|wget|python-requests|python-urllib|aiohttp|Go-http-client|okhttp|libwww-perl|node-fetch|axios"
    r"|HeadlessChrome|facebookexternalhit|Prefetch Proxy|Discord|Slack|WhatsApp|Applebot|Bytespider)",
    re.I,
)
CLAIMS = {
    "google": (re.compile(r"google|adsbot|apis-google|mediapartners|storebot", re.I), (".googlebot.com", ".google.com")),
    "bing": (re.compile(r"bingbot|bingpreview|msnbot|adidxbot", re.I), (".search.msn.com",)),
}
CACHE = os.environ.get("RDNS_CACHE", "/var/lib/aiondps-logreport/rdns.json")


def bot_name(ua):
    m = NAMED.search(ua) or TOOLS.search(ua)
    return m.group(1) if m else None


def claim_of(name):
    for key, (rx, _) in CLAIMS.items():
        if rx.search(name):
            return key
    return None


def rdns_verdict(ip, suffixes):
    """echt / gefälscht / unklar (no PTR record)."""
    try:
        host = socket.gethostbyaddr(ip)[0].lower()
    except OSError:
        return "unklar"
    if not host.endswith(suffixes):
        return "gefälscht"
    try:
        forward = {a[4][0] for a in socket.getaddrinfo(host, None)}
    except OSError:
        return "unklar"
    return "echt" if ip in forward or ip.lower() in forward else "gefälscht"


def mask(ip):
    if ":" in ip:
        return str(ipaddress.ip_network(f"{ip}/48", strict=False))
    return ".".join(ip.split(".")[:3]) + ".x"


def main(path):
    now = datetime.now().astimezone()
    rows = []
    with open(path, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = LINE.match(line)
            if not m:
                continue
            ip, ts, req, status, _ref, ua = m.groups()
            name = bot_name(ua)
            if not name:
                continue
            parts = req.split(" ")
            url = parts[1].split("?")[0] if len(parts) > 1 else req
            rows.append((ip, datetime.strptime(ts, "%d/%b/%Y:%H:%M:%S %z"), url, int(status), name))

    try:
        with open(CACHE) as fh:
            cache = json.load(fh)
    except (OSError, ValueError):
        cache = {}
    todo = {}
    for ip, _t, _u, _s, name in rows:
        key = claim_of(name)
        if key and f"{key}|{ip}" not in cache:
            todo[f"{key}|{ip}"] = (ip, CLAIMS[key][1])
    with ThreadPoolExecutor(16) as pool:
        for k, v in zip(todo, pool.map(lambda a: rdns_verdict(*a), todo.values())):
            cache[k] = v
    try:
        os.makedirs(os.path.dirname(CACHE), exist_ok=True)
        with open(CACHE, "w") as fh:
            json.dump(cache, fh)
    except OSError:
        pass

    groups = defaultdict(lambda: {"n": 0, "d1": 0, "d7": 0, "ips": Counter(), "first": None, "last": None,
                                  "codes": Counter(), "robots": 0, "sitemap": 0, "paths": Counter(),
                                  "days": Counter()})
    for ip, t, url, status, name in rows:
        key = claim_of(name)
        verdict = cache.get(f"{key}|{ip}") if key else "Eigenangabe"
        g = groups[(re.sub(r"/.*", "", name), verdict)]
        g["n"] += 1
        age = now - t
        g["d1"] += age <= timedelta(days=1)
        g["d7"] += age <= timedelta(days=7)
        g["ips"][mask(ip)] += 1
        g["first"] = min(g["first"] or t, t)
        g["last"] = max(g["last"] or t, t)
        g["codes"][status // 100] += 1
        g["robots"] += url == "/robots.txt"
        g["sitemap"] += url == "/sitemap.xml"
        g["paths"][url] += 1
        g["days"][t.strftime("%Y-%m-%d")] += 1

    ordered = sorted(groups.items(), key=lambda kv: -kv[1]["n"])
    days = [(now - timedelta(days=i)).strftime("%Y-%m-%d") for i in range(13, -1, -1)]
    fmt = lambda t: t.strftime("%d.%m. %H:%M")
    badge = {"echt": "ok", "gefälscht": "bad", "unklar": "warn", "Eigenangabe": "dim"}

    out = [f"""<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="robots" content="noindex,nofollow"><title>Bots – aiondps.com</title><style>
:root{{--bg:#14161b;--fg:#e6e8ee;--dim:#8b92a3;--card:#1c1f27;--line:#2b303b;--ok:#4cc38a;--bad:#ef6a5b;--warn:#e5b94c}}
@media(prefers-color-scheme:light){{:root{{--bg:#f5f6f8;--fg:#1b1e25;--dim:#606878;--card:#fff;--line:#dde0e7;--ok:#1f9d63;--bad:#d1432f;--warn:#a87a0a}}}}
body{{margin:0;padding:24px 16px;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif}}
main{{max-width:1872px;margin:auto}}a{{color:inherit}}h1{{font-size:20px}}h2{{font-size:16px;margin:28px 0 8px}}
.wrap{{overflow-x:auto;background:var(--card);border:1px solid var(--line);border-radius:10px}}
table{{border-collapse:collapse;width:100%}}th,td{{padding:6px 10px;border-bottom:1px solid var(--line);text-align:left;vertical-align:top;white-space:nowrap}}
th{{color:var(--dim);font-weight:600;font-size:12px}}td.n,th.n{{text-align:right;font-variant-numeric:tabular-nums}}
.b{{padding:1px 8px;border-radius:99px;font-size:12px;border:1px solid currentColor}}.ok{{color:var(--ok)}}.bad{{color:var(--bad)}}.warn{{color:var(--warn)}}.dim{{color:var(--dim)}}
td.p{{white-space:normal;max-width:420px;color:var(--dim);font-size:12px}}p.s{{color:var(--dim)}}</style></head><body><main>
<h1>Bots auf aiondps.com</h1><p class="s"><a href="./">← Übersicht</a> · Stand {html.escape(now.strftime('%d.%m.%Y %H:%M'))} · {len(rows)} Bot-Anfragen im Log ·
<span class="b ok">echt</span> = Reverse-DNS bestätigt (nur Google/Bing prüfbar) · <span class="b bad">gefälscht</span> = gibt sich als Google/Bing aus, kommt aber von anderswo ·
<span class="b warn">unklar</span> = kein PTR-Eintrag · <span class="b dim">Eigenangabe</span> = nur der User-Agent, nicht prüfbar. /metrics ist ausgeblendet.</p>
<h2>Wer kommt wie oft</h2><div class="wrap"><table><tr><th>Bot<th>Status<th class="n">Gesamt<th class="n">24 h<th class="n">7 Tage<th class="n">2xx<th class="n">3xx<th class="n">4xx<th class="n">5xx
<th class="n">robots<th class="n">sitemap<th>Erster Besuch<th>Letzter Besuch<th>IP-Bereiche<th>Meistgeholt</tr>"""]
    for (name, verdict), g in ordered:
        c = g["codes"]
        top = ", ".join(f"{html.escape(p)} ({n})" for p, n in g["paths"].most_common(4))
        ips = ", ".join(f"{i} ({n})" for i, n in g["ips"].most_common(3))
        out.append(
            f'<tr><td>{html.escape(name)}<td><span class="b {badge[verdict]}">{verdict}</span>'
            f'<td class="n">{g["n"]}<td class="n">{g["d1"]}<td class="n">{g["d7"]}'
            f'<td class="n">{c[2]}<td class="n">{c[3]}<td class="n">{c[4]}<td class="n">{c[5]}'
            f'<td class="n">{g["robots"]}<td class="n">{g["sitemap"]}<td>{fmt(g["first"])}<td>{fmt(g["last"])}'
            f'<td>{html.escape(ips)}<td class="p">{top}</tr>')
    out.append('</table></div><h2>Anfragen pro Tag (die zehn aktivsten, ohne Fälschungen)</h2><div class="wrap"><table><tr><th>Bot'
               + "".join(f'<th class="n">{d[5:]}' for d in days) + "</tr>")
    shown = [kv for kv in ordered if kv[0][1] != "gefälscht"][:10]
    for (name, verdict), g in shown:
        out.append(f"<tr><td>{html.escape(name)}" + "".join(f'<td class="n">{g["days"][d] or "·"}' for d in days) + "</tr>")
    out.append("</table></div></main></body></html>")
    sys.stdout.write("\n".join(out))


if __name__ == "__main__":
    main(sys.argv[1])
