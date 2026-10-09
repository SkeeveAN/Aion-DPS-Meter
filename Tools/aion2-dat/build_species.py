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
    99: "BackAttackDefense", 408: "FeralBlock", 411: "NatureAccuracy", 413: "NatureCritical", 421: "TransCritical",
    # Not yet seen in a screenshot, but each species block runs atk, def, accuracy, evasion, crit, crit resist, -, block.
    403: "FeralAccuracy", 405: "FeralCritical", 409: "NatureDamage", 417: "TransDamage",
    # Cognia block (2026-10-09, Sidy's screenshots): 393 attack, 395 accuracy, 396 evasion, 400 block; 588 = Verteidigung (Frontal).
    393: "IntellectDamage", 395: "IntellectAccuracy", 396: "IntellectEvasion", 400: "IntellectBlock", 588: "FrontAttackDefense",
    # Specia (Sidy's upload 2026-10-09 16:10 == the screenshot): 443 Wucht 0,2 %, 38 Krit.-Angriffskraft, 47 Krit.-Schadensresistenz 1,3 %.
    443: "HardHit", 38: "CriticalAddDamage", 47: "DecreaseCriticalDamage",
    # Matched against the client's VehicleCreatureOption table (value range per species/slot/grade; 55/55 known ids reproduce): unique candidates.
    133: "CriticalResist", 284: "DefensePierce", 101: "BackAttackCriticalResist",
    # By elimination (every other candidate already has an id): 41, 196. 379/380 = the PvE amplify/decrease pair (likely, not proven).
    41: "CriticalDamageDefense", 196: "MaxHPRatio", 379: "PvEAmplifyDamage", 380: "PvEDecreaseDamage",
    # The EStat enum order (a2meter/Aion2Meter StatMapping.cs, from a .usmap) matches every id above with a step-wise offset (4, 5, 6, 8, 9, 10, 11);
    # it gives the last open ones: 28, 70, 159, 449; 589/590 and 102/103 follow the Front/BackAttack families.
    28: "AmplifyAllDamage", 70: "DecreaseDamage", 159: "AbnormalResistance", 449: "IgnoreIronWall",
    589: "AmplifyFrontAttack", 590: "DecreaseFrontAttack", 102: "AmplifyBackAttack", 103: "DecreaseBackAttack",
    587: "FrontAttackDamage", 591: "FrontAttackCritical", 592: "FrontAttackCriticalResist",
}
PERCENT = {44, 47, 158, 443, 445}
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
