"""Builds Client/assets/aion2/stats/stat_sheet.json: the layout of the game's own stat window (Attribut-Details > Stats) with the stat-frame ids.

Usage:  python3 build_stat_sheet.py <l10n dir (extract_l10n.py output)> <out.json>

Names are the client's texts (String_StatName_<Token>_body) in every language. The ids were matched against the stat window of a character
(screenshots of 13 November 2026, every number reproduced from the frame): most rows are one frame id ("fmt" p = hundredths of a percent, "div" a divisor);
rows with "sum" are put together the way the game does it: (sum of weight x id) x (1 + ratio id / 10000) / div. The ratio ids are the game's own
"... +" stats (425 attack, 426 defence, 427 accuracy, 428 evasion, 429 crit, 430 crit resist, 432 block, 196 HP, 197 MP).
"""
import json
import sys

l10n_dir, out = sys.argv[1:3]
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
tables = {code: json.load(open(f"{l10n_dir}/l10n_{loc}.json", encoding="utf-8")) for code, loc in LANGS.items()}


def names(key):
    return {code: t[key] for code, t in tables.items() if key in t}


def stat(token):
    return names(f"String_StatName_{token}_body")


def row(token, id=None, fmt="n", sum=None, mult=None, div=1, name=None):
    r = {"names": stat(name or token), "fmt": fmt}
    if sum:
        r["sum"] = [[i, w] for i, w in sum]
        if mult:
            r["mult"] = mult
    else:
        r["id"] = id
    if div != 1:
        r["div"] = div
    if not r["names"]:
        raise SystemExit("no text for " + (name or token))
    return r


def part(ids, mult=None, **kw):
    return {"sum": [(i, 1) for i in ids], "mult": mult, **kw}


groups = [
    ("total", "overview", [
        row("WeaponFixingDamage", sum=[(317, 1), (19, 1), (31, .5), (33, .5)], mult=425),
        row("ArmorDefense", sum=[(52, 1), (307, 1)], mult=426),
        row("WeaponAccuracy", sum=[(104, 1), (318, 1)], mult=427, fmt="n"),
        row("ArmorEvasion", sum=[(116, 1), (312, 1)], mult=428),
        row("Critical", sum=[(128, 1)], mult=429),
        row("CriticalResist", sum=[(133, 1)], mult=430),
        dict(row("HPMax", sum=[(193, 1)], mult=196), ceil=True),
        dict(row("MPMax", sum=[(199, 1)], mult=197), ceil=True),
        row("CombatSpeed", 282, "p"),
        row("MoveSpeed", 205, "p"),
    ]),
    ("combat", "Kampf", [
        row("DefensePierce", 284), row("SealStoneAddDamage", 69), row("CriticalAddDamage", 38), row("CriticalDamageDefense", 41),
        row("BackAttackDamage", 98), row("BackAttackDefense", 99), row("FrontAttackDamage", 587), row("FrontAttackDefense", 588),
        row("AmplifyAllDamage", 28, "p"), row("DecreaseDamage", 70, "p"), row("AmplifyWeaponDamage", 32, "p"),
        row("AmplifyCriticalDamage", 44, "p"), row("DecreaseCriticalDamage", 47, "p"),
        row("AmplifyBackAttack", 102, "p"), row("DecreaseBackAttack", 103, "p"), row("AmplifyFrontAttack", 589, "p"), row("DecreaseFrontAttack", 590, "p"),
    ]),
    ("hits", "Trefferwertung", [
        row("AdditionalHitRate", 146, "p"), row("AdditionalHitResistRate", 147, "p"),
        row("BackAttackCritical", 100), row("BackAttackCriticalResist", 101), row("FrontAttackCritical", 591), row("FrontAttackCriticalResist", 592),
        row("BlockPierce", 256), row("Block", sum=[(255, 1)], mult=432),
        row("IgnoreIronWall", 449, "p"), row("IronWall", 445, "p"), row("IgnoreRestoration", 448, "p"), row("Restoration", 444, "p"),
        row("Perfect", 442, "p"), row("PerfectResist", 446, "p"), row("HardHit", 443, "p"), row("HardHitResist", 447, "p"),
    ]),
    ("status", "Statuseffekte", [
        row("StunAccuracy", sum=[(158, 1), (321, 1)], fmt="p"), row("AbnormalAccuracy", 158, "p"), row("AbnormalResistance", 159, "p"),
    ]),
    ("pvp", "PvP", [
        row("PvPAddDamage", 58), row("PvPDamageDefense", 61), row("PvPAmplifyDamage", 65, "p"), row("PvPDecreaseDamage", 76, "p"),
        row("PvPAccuracy", 113), row("PvPEvasion", 125), row("PvPCritical", 132), row("PvPCriticalResist", 136),
        row("PvPBlock", sum=[(569, 1)], mult=432),
    ]),
    ("pve", "PvE", [
        row("PvEAddDamage", 56), row("PvEDamageDefense", 57), row("PvEAccuracy", 110), row("PvEEvasion", 122),
        row("PvEAmplifyDamage", 379, "p"), row("PvEDecreaseDamage", 380, "p"),
        row("BossNpcAddDamage", 50), row("BossNpcDefense", 51), row("BossNpcAmplifyDamage", 520, "p"), row("BossNpcDecreaseDamage", 521, "p"),
    ]),
    ("faction", "Fraktion", [
        row(t, i) for i, t in [
            (393, "IntellectDamage"), (394, "IntellectDefense"), (395, "IntellectAccuracy"), (396, "IntellectEvasion"), (397, "IntellectCritical"), (398, "IntellectCriticalResist"), (400, "IntellectBlock"),
            (401, "FeralDamage"), (402, "FeralDefense"), (403, "FeralAccuracy"), (404, "FeralEvasion"), (405, "FeralCritical"), (406, "FeralCriticalResist"), (408, "FeralBlock"),
            (409, "NatureDamage"), (410, "NatureDefense"), (411, "NatureAccuracy"), (412, "NatureEvasion"), (413, "NatureCritical"), (414, "NatureCriticalResist"), (416, "NatureBlock"),
            (417, "TransDamage"), (418, "TransDefense"), (419, "TransAccuracy"), (420, "TransEvasion"), (421, "TransCritical"), (422, "TransCriticalResist"), (424, "TransBlock"),
        ]
    ]),
    ("special", "Spezial", [
        row("GroundVehicleSpeed", 208), row("VehicleSprintCost", 210, "p"),
        row("CoolTimeIncrease", sum=[(215, -1), (385, 1)], fmt="p"),
    ]),
    ("resources", "Ressourcen", [
        row("SPMax", 242, div=100), row("FPMax", 298, div=100),
        row("BattleHPRegen", sum=[(194, 1), (275, 1)]), row("RestHPRegen", sum=[(194, 1), (195, 1)]),
        row("HpPotionRegen", 221), row("HpPotionRate", 220, "p"),
        row("BattleMPRegen", sum=[(200, 1), (276, 1)]), row("RestMPRegen", sum=[(200, 1), (201, 1)]),
        row("MPUseIncrease", 386, "p"),
        row("BattleSPRegen", sum=[(243, 1), (277, 1)], div=100), row("RestSPRegen", sum=[(243, 1), (244, 1)], div=100),
        row("BattleFPRegen", 300, div=100), row("RestFPRegen", 301, div=100),
    ]),
]
result = {"groups": [{"id": g, "title": t, "rows": rows} for g, t, rows in groups]}
json.dump(result, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(sum(len(g[2]) for g in groups), "rows")
