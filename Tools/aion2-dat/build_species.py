"""Builds Backend/src/data/aion2/species_stats.json from the language tables extract_l10n.py wrote.

Usage:  python3 build_species.py <dir with l10n_<locale>.json> <out json>

The client's species knowledge frame (opcode 0x0090) carries numeric stat ids; the game's text tables name the
stats by token (String_StatName_<Token>_body). The id -> token pairs below were matched against the pet window's
screenshots (Cognia, Fera, Natura, Varia, Specia, 2026-10-05): every id appears there with its German name, and
the German name is unique among the tokens. Ids not listed show as "Stat <id>" on the website until added.
"percent" stats are sent in hundredths (Endurance 145 = 1.45 %).
"""
import json
import os
import sys

STATS = {
    31: "WeaponDamage", 51: "BossNpcDefense", 56: "PvEAddDamage", 69: "SealStoneAddDamage", 100: "BackAttackCritical",
    116: "Evasion", 122: "PvEEvasion", 158: "AbnormalAccuracy", 394: "IntellectDefense", 402: "FeralDefense",
    406: "FeralCriticalResist", 422: "TransCriticalResist",
    19: "FixingDamage", 44: "AmplifyCriticalDamage", 50: "BossNpcAddDamage", 52: "Defense", 57: "PvEDamageDefense",
    98: "BackAttackDamage", 104: "Accuracy", 110: "PvEAccuracy", 128: "Critical", 193: "HPMax", 199: "MPMax", 255: "Block",
    397: "IntellectCritical", 398: "IntellectCriticalResist", 401: "FeralDamage", 404: "FeralEvasion",
    410: "NatureDefense", 412: "NatureEvasion", 414: "NatureCriticalResist", 416: "NatureBlock",
    418: "TransDefense", 419: "TransAccuracy", 420: "TransEvasion", 424: "TransBlock", 445: "IronWall",
    587: "FrontAttackDamage", 591: "FrontAttackCritical", 592: "FrontAttackCriticalResist",
}
PERCENT = {44, 158, 445}
SPECIES = {2: ("cognia", "INTELLECT"), 3: ("fera", "FERA"), 4: ("natura", "NATURE"), 5: ("varia", "TRANS"), 6: ("specia", "SPECIAL")}
LOCALES = {"de": "de-DE", "en": "en-US", "es": "es-ES", "fr": "fr-FR", "ru": "ru-RU"}

src, out = sys.argv[1], sys.argv[2]
tables = {code: json.load(open(os.path.join(src, f"l10n_{loc}.json"), encoding="utf-8")) for code, loc in LOCALES.items()}


def names(key):
    return {code: table[key] for code, table in tables.items() if key in table}


result = {
    "species": {str(i): {"key": key, "names": names(f"String_UI_STATINFO_GROWTH_SUMMARY_PET_{token}_body")} for i, (key, token) in SPECIES.items()},
    "stats": {str(i): {"names": names(f"String_StatName_{token}_body"), **({"percent": True} if i in PERCENT else {})} for i, token in STATS.items()},
}
with open(out, "w", encoding="utf-8") as f:
    json.dump(result, f, ensure_ascii=False, indent=1, sort_keys=False)
print(len(result["stats"]), "stats,", len(result["species"]), "species")
