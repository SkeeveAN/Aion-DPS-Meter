"""Builds Backend/src/data/aion2/item_stats.json: the names of the stats that appear on equipment entries
(mana stones and rolled stats of a piece, see Aion2FrameDecoder.TryReadItemDetails).

Usage:  python3 build_item_stats.py <dir with l10n_<locale>.json> <out json> [godstones out json]

An entry of the equipment list names a stat by the game's numeric stat id; the text tables name the stats by token
(String_StatName_<Token>_body). The id -> token pairs below were matched against in-game tooltips of 2026-10-09
(Aahz' Aulamus' Earrings: Block / Angriffskraft / Zusatzausweichen / Verteidigung stones, MP 96, MP-Regeneration 23,
Angriffskraft 24, Ausweichen 24; an Enraged Kromede Earring: Kritischer Treffer / Verteidigung / MP stones, LP 213,
MP-Regeneration 27, Block 31) and against the species table's ids (build_species.py, matched there against the pet
window). The ImprintMagicStoneProb table of the client names the stone stats by the same tokens (EStat::Block, Evasion,
Accuracy, Critical, CriticalResist, HPMax, MPMax, ArmorDefense, WeaponFixingDamage ...). Ids that no screenshot has
confirmed (the small ids 1-6 look like the six base attributes) stay out and show as "Stat <id>" on the website.
"percent" stats are sent in hundredths (Endurance 145 = 1.45 %).
"""
import json
import os
import sys

STATS = {
    5: "AGI", 282: "CombatSpeed",  # Praezision 26 and Kampftempo 10,7 % of the greatsword tooltip (2026-10-09)
    19: "FixingDamage", 38: "CriticalAddDamage", 47: "DecreaseCriticalDamage", 52: "Defense",
    98: "BackAttackDamage", 99: "BackAttackDefense", 104: "Accuracy", 116: "Evasion", 128: "Critical", 133: "CriticalResist",
    158: "AbnormalAccuracy", 193: "HPMax", 199: "MPMax", 200: "MPRegen", 255: "Block",
    307: "ArmorDefense", 312: "ArmorEvasion", 317: "WeaponFixingDamage", 445: "IronWall", 588: "FrontAttackDefense",
}
PERCENT = {47, 158, 282, 445}
# What a mana stone adds, by stat id and tier (1 white, 2 green, 3 blue): only combinations seen in an in-game tooltip, nothing
# extrapolated (Block+7 / MP+15 white shoulders, Block+10 / MP+30 green earring and bracelet, LP+30 white greatsword).
STONE_VALUES = {255: {1: 7, 2: 10}, 199: {1: 15, 2: 30}, 193: {1: 30}}
LOCALES = {"de": "de-DE", "en": "en-US", "es": "es-ES", "fr": "fr-FR", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}

src, out = sys.argv[1], sys.argv[2]
tables = {code: json.load(open(os.path.join(src, f"l10n_{loc}.json"), encoding="utf-8")) for code, loc in LOCALES.items()}
result = {}
for stat_id, token in STATS.items():
    key = f"String_StatName_{token}_body"
    names = {code: table[key] for code, table in tables.items() if key in table}
    if "en" not in names:
        sys.exit(f"no text for stat {stat_id} ({token})")
    result[str(stat_id)] = {"names": names, **({"percent": True} if stat_id in PERCENT else {}),
                            **({"stone": {str(t): v for t, v in STONE_VALUES[stat_id].items()}} if stat_id in STONE_VALUES else {})}
with open(out, "w", encoding="utf-8") as f:
    json.dump(result, f, ensure_ascii=False, indent=1, sort_keys=False)
    f.write("\n")
print(f"{len(result)} stats written to {out}")

# Godstones: the entry carries the item id; 19950001.. are the rare weapon godstones (19950016 Aulvicars Zauber and 19950018
# Rathmans Gier seen on real pieces; the number is the running number of String_STR_GODSTONE_WEAPON_RARE_<nn>).
if len(sys.argv) > 3:
    gods = {}
    for n in range(1, 19):
        key = f"String_STR_GODSTONE_WEAPON_RARE_{n:02d}_body"
        names = {code: table[key] for code, table in tables.items() if key in table}
        if names:
            gods[str(19950000 + n)] = names
    with open(sys.argv[3], "w", encoding="utf-8") as f:
        json.dump(gods, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print(f"{len(gods)} godstones written to {sys.argv[3]}")
