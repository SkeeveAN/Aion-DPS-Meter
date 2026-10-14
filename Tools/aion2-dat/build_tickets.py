"""Builds Client/assets/aion2/tickets/tickets.json: the counted entry tickets / reward counts / energy of the account-status frames
(opcodes 0x0b61 list at login, 0x0c61 changes).

Usage:  python3 build_tickets.py <l10n dir (extract_l10n.py output)> <contents_tickets.json> <out.json>

Names come from the game's own text in every language (the key whose English text equals the ticket's name; several tickets share one text).
The numbers per ticket (cap, cap with the subscription, per character/server, daily and weekly refill) come from the ContentsTicket table as
another tool dumped it (a table of the game's own data: ids, caps, reset times); the caps were checked against the login lists of our own
recordings (28/32 keys, 35/28 boss kills, 14 and 7 reward counts, 540 of 560 Odyle energy ...). Time-type tickets (access hours) have no count.
"""
import collections
import json
import sys

l10n_dir, tickets_path, out = sys.argv[1:4]
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
tables = {code: json.load(open(f"{l10n_dir}/l10n_{loc}.json", encoding="utf-8")) for code, loc in LANGS.items()}
by_text = collections.defaultdict(list)
for key, value in tables["en"].items():
    by_text[value].append(key)

def pick(keys, internal):
    wanted = internal.lower().replace("contents_ticket_", "").replace("contents_key_", "").replace("_", "")
    for key in keys:
        if wanted and wanted in key.lower().replace("_", "") and key.startswith("String_"):
            return key
    for key in keys:
        if key.startswith("String_"):
            return key
    return keys[0]

# the short names of the content (the menu texts of the client): what the checklist's overview calls an instance or mini event
SHORT_KEYS = {
    1: "InputKeyMapping_UI_ShugoFesta_desc",
    7: "InputKeyMapping_UI_ShugoFesta_desc",
    8: "Post_10120101_sender_name",
    4: "String_UI_MENU_BUTTON_BOSSCHALLENGE_body",
    80000001: "String_UI_AWAKEN_TITLE_body",
    70000001: "String_STR_MATCHING_TYPE_SUPPRESSION_TITLE_body",
}
DAILY_DUNGEON_KEY = "String_UI_DAILYDUNGEON_TITLE_body"  # "Daily Dungeon": the weekly entry count of the Unknown Fissure (ticket 101) is the daily dungeon's counter

source = json.load(open(tickets_path, encoding="utf-8"))["Global"]
result = []
for tid, t in sorted(source.items(), key=lambda kv: int(kv[0])):
    key = pick(by_text[t["en"]], t["internal"])
    names = {code: tables[code][key] for code in LANGS if key in tables[code]}
    entry = {
        "id": int(tid),
        "internal": t["internal"],
        "names": names,
        "max": t["max"],
        "subscribeMax": t["subscribeMax"],
        "perServer": t["owner"] == "Server",
        "time": t["type"] == "Time",
    }
    short = None
    if int(tid) in SHORT_KEYS:
        short = {code: tables[code][SHORT_KEYS[int(tid)]] for code in LANGS if SHORT_KEYS[int(tid)] in tables[code]}
    elif int(tid) == 101:
        short = {code: tables[code][DAILY_DUNGEON_KEY] for code in LANGS if DAILY_DUNGEON_KEY in tables[code]}
    if short:
        entry["short"] = short
    if t.get("daily"):
        entry["daily"] = {"at": t["daily"]["at"], "count": t["daily"]["count"]}
    if t.get("weekly"):
        entry["weekly"] = {"at": t["weekly"]["at"], "count": t["weekly"]["count"], "day": t["weekly"]["day"]}
    result.append(entry)

json.dump({"_comment": "built by Tools/aion2-dat/build_tickets.py; names = the game's own texts", "tickets": result}, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(len(result), "tickets ->", out)

# the two mandatory scrolls the NPC sells every week (the checklist counts the purchases by hand): the item names of the client in every language
SCROLLS = {
    "verteron": "String_STR_ITEM_QUESTSCROLL_DUTY_L1_VERTERON_01_body",
    "abyss": "String_STR_ITEM_QUESTSCROLL_DUTY_L1_ABYSS_COMMON_01_body",  # the four kinds (common to unique) are named "<name>", ": Veteran", ": Elite", ": Special Mission"
}
import re
# the trailing "(Bound)" (in each language its own word) is left out: the checklist shows the scroll's name
scrolls = {kind: {code: re.sub(r"\s*[(（][^()（）]*[)）]\s*$", "", tables[code][key]) for code in LANGS if key in tables[code]} for kind, key in SCROLLS.items()}
scroll_path = out.replace("tickets.json", "scrolls.json")
json.dump(scrolls, open(scroll_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("scrolls ->", scroll_path)
