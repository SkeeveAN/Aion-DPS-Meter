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
confirmed stay out and show as "Stat <id>" on the website (1 = Macht is confirmed; 2, 3, 4 and 6 are the other base attributes by the
numbering of the stat frame, where 5 = Praezision matched).
"percent" stats are sent in hundredths (Endurance 145 = 1.45 %).
"""
import json
import os
import sys

STATS = {
    1: "STR",  # Macht 31 of the greatsword tooltip, confirmed by the player 2026-10-20 (the website showed "Stat 1")
    2: "DEX", 3: "INT", 4: "CON", 6: "WIS",  # the other base attributes: the stat frame's ids 1..6 (Aion2Attributes), same numbering as 5 = AGI; not yet seen on a piece
    5: "AGI", 282: "CombatSpeed",  # Praezision 26 and Kampftempo 10,7 % of the greatsword tooltip (2026-10-09)
    19: "FixingDamage", 38: "CriticalAddDamage", 41: "CriticalDamageDefense",  # Krit.-Verteidigung 157 of the Nuakum Gloves, confirmed by the player 2026-10-20
    47: "DecreaseCriticalDamage", 52: "Defense",
    98: "BackAttackDamage", 99: "BackAttackDefense", 104: "Accuracy", 116: "Evasion", 128: "Critical", 133: "CriticalResist",
    158: "AbnormalAccuracy", 193: "HPMax", 199: "MPMax", 200: "MPRegen", 255: "Block",
    # The rest follows from the EStat enum order (a2meter/Aion2Meter StatMapping.cs, 557 names) with the step-wise offset between id and enum index
    # (+1 up to id 18, +4 up to 51, +5 up to 127, +6 up to 145, +8 up to 254, +9 up to 281 and on to 318, +10 from 393, +11 at 443; the steps sit where the
    # client has a stat the list lacks: Defense 52, Critical 128, Block 255, Perfect 442, IronWall 445). Checked by the player on gear tooltips 2026-10-20:
    # 194 LP-Regeneration, 159 Widerstand (Statuseffekte) 3,3 %, 442 Perfektion 2,4 %, 587 Angriffskraft (Frontal) 97. Not named yet: 136, 319-392 (position of a step unknown).
    28: "AmplifyAllDamage", 32: "AmplifyWeaponDamage", 58: "PvPAddDamage", 61: "PvPDamageDefense", 70: "DecreaseDamage", 74: "DecreaseWeaponDamage",
    102: "AmplifyBackAttack", 113: "PvPAccuracy", 125: "PvPEvasion", 132: "PvPCritical", 146: "AdditionalHitRate", 147: "AdditionalHitResistRate",
    159: "AbnormalResistance", 194: "HPRegen", 205: "MoveSpeed", 235: "AmplifyHpHealGet", 318: "WeaponAccuracy", 425: "DamageRatio", 426: "DefenseRatio",
    442: "Perfect", 443: "HardHit", 587: "FrontAttackDamage", 44: "AmplifyCriticalDamage",
    # 442..449 in the game's own order: Perfect, HardHit, Restoration, IronWall, PerfectResist, HardHitResist, IgnoreRestoration, IgnoreIronWall (the enum list lacks Perfect,
    # Restoration and IronWall). 444 = Regeneration 1,5 % seen on Hanya's Red Agate Breastplate 2026-10-20; 449 was anchored before by the species table.
    444: "Restoration", 446: "PerfectResist", 447: "HardHitResist", 448: "IgnoreRestoration", 449: "IgnoreIronWall",
    298: "FPMax",  # "FP" (Flight Power) 272 on Kromede Earrings, sent as 27193 = hundredths (confirmed by the player 2026-10-20)
    307: "ArmorDefense", 312: "ArmorEvasion", 317: "WeaponFixingDamage", 445: "IronWall", 588: "FrontAttackDefense",
}
PERCENT = {47, 158, 282, 445, 28, 32, 44, 70, 74, 102, 146, 147, 159, 205, 235, 425, 426, 442, 443, 444, 446, 447, 448, 449}  # 159 and 442 seen as 3,3 % and 2,4 % in the game; the rest by name and size
# sent in hundredths but shown as a whole number (FP 27193 -> 272)
HUNDREDTHS = {298}
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
                            **({"hundredths": True} if stat_id in HUNDREDTHS else {}),
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
