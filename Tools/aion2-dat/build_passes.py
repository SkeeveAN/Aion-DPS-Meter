"""Builds Client/assets/aion2/tickets/passes.json: the Daeva passes (battle passes and event passes) of the global server.

Usage:  python3 build_passes.py <BattlePassGroup.json (decoded table)> <l10n dir (extract_l10n.py output)> <out.json> [first_id]

One entry per pass of the Elyos side (ids 10xxx) with its Asmodian twin (20xxx): the id of the entry in the pass frame 0x24e3 for both sides,
the weekly experience limit, the end (on the clock the game shows, 16:00 UTC = 09:00) and the names in every language from the game's own text.
The ticket id (99000010 + position) is the id of our own ticket that holds the reading; the first two keep the ids they always had.
Levels: only where the cost of the levels is known (see Aion2Tickets.PassLevelOf); the others show the experience in total.
Run this after every client patch (new passes show up in BattlePassGroup) and publish the file; clients take it without a release.
"""
import datetime
import json
import sys

table_path, l10n_dir, out = sys.argv[1:4]
first_id = int(sys.argv[4]) if len(sys.argv) > 4 else 10044
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
tables = {}
for code, loc in LANGS.items():
    d = json.load(open(f"{l10n_dir}/l10n_{loc}.json", encoding="utf-8"))
    tables[code] = next(iter(d.values())) if isinstance(d, dict) and len(d) < 5 else d

rows = json.load(open(table_path, encoding="utf-8"))["Properties"]["Data"]
by_id = {r["ID"]["Value"]: r for r in rows}

def end_clock(row):
    utc = datetime.datetime(1, 1, 1) + datetime.timedelta(microseconds=row["EndTime"]["Ticks"] // 10)
    return (utc - datetime.timedelta(hours=7)).strftime("%Y-%m-%dT%H:%M")

def names(row):
    key = "String_" + row["Title"]["Key"] + "_body"
    return {code: tables[code].get(key) or tables["en"].get(key, "") for code in LANGS}

# the levels whose cost is known (checked in the game): first level, cost of the next ones, experience below which the level is known
LEVELS = {10044: {"first": 400, "step": 1600, "known": 24400}, 10045: {"first": 700, "step": 700, "known": 10500}}

passes = []
for light_id in sorted(i for i in by_id if i >= first_id and i < 20000 and by_id[i]["ExpLimitType"].endswith("Weekly")):
    dark_id = light_id + 10000
    if dark_id not in by_id:
        continue
    light = by_id[light_id]
    entry = {
        "ticket": 99000010 + len(passes),
        "light": light_id,
        "dark": dark_id,
        "limit": light["ExpLimitValue"],
        "end": end_clock(light),
        "names": {"light": names(light), "dark": names(by_id[dark_id])},
    }
    if light_id in LEVELS:
        entry["levels"] = LEVELS[light_id]
    passes.append(entry)

json.dump({"passes": passes}, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(len(passes), "passes ->", out)
