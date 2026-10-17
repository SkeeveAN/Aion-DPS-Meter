"""Builds Client/assets/aion2/stats/attributes.json: the game's own texts for the attribute tool (tool "Attributes").

Usage:  python3 build_attributes.py <dir with l10n_<locale>.json from extract_l10n.py> <out.json>

Keys of the output (each maps a language code to the text; a text with several lines uses \\n):
  desc.<node>   what an attribute or Lord does    (String_UI_desc_<STR|DEX|..|Destruction|..>; the first line of a Lord, its own name, is dropped)
  stat.<Enum>   name of a stat                     (String_StatName_<Enum>_body)
  tip.<Enum>    tooltip of a stat                  (String_UI_desc_<Enum>; colour tags removed)
  grp.<name>    heading of a group of the stat page (String_UI_CHARACTERSTATUS_SUBTITLE_<name>_body)
"""
import json
import os
import re
import sys

locales = {"de": "de-DE", "en": "en-US", "es": "es-ES", "fr": "fr-FR", "ja": "ja-JP", "ko": "ko-KR", "pt": "pt-BR", "ru": "ru-RU"}
texts = {code: json.load(open(os.path.join(sys.argv[1], f"l10n_{loc}.json"), encoding="utf-8")) for code, loc in locales.items()}

NODES = {"str": "STR", "dex": "DEX", "agi": "AGI", "wis": "WIS", "int": "INT", "con": "CON", "justice": "Justice", "destruction": "Destruction",
         "death": "Death", "wisdom": "Wisdom", "destiny": "Destiny", "space": "Space", "time": "Time", "life": "Life", "illusion": "Illusion", "freedom": "Freedom"}
# every stat the tool names or explains (enum names of the game)
STATS = """DamageRatio EvasionRatio BlockRatio CriticalResistRatio AccuracyRatio CriticalRatio AbnormalResistance AbnormalAccuracy MaxHPRatio DefenseRatio
MPUseIncrease MaxMPRatio HardHitResist CoolTimeIncrease CombatSpeed MoveSpeed Perfect PerfectResist IgnoreRestoration Restoration HardHit IronWall IgnoreIronWall
WeaponFixingDamage ArmorDefense WeaponAccuracy ArmorEvasion Critical CriticalResist HPMax MPMax DefensePierce SealStoneAddDamage CriticalAddDamage CriticalDamageDefense
BackAttackDamage BackAttackDefense FrontAttackDamage FrontAttackDefense AmplifyAllDamage DecreaseDamage AmplifyWeaponDamage AmplifyCriticalDamage DecreaseCriticalDamage
AdditionalHitRate BackAttackCritical BackAttackCriticalResist FrontAttackCritical FrontAttackCriticalResist BlockPierce Block""".split()
GROUPS = ["Bookmark", "Attack", "Resist"]


def clean(value):
    value = re.sub(r"</?[A-Za-z]*>", "", value)
    return "\n".join(line.strip() for line in value.split("\n") if line.strip())


out = {}
missing = []
def put(key, source, drop_first=False):
    row = {}
    for code, table in texts.items():
        value = table.get(source)
        if value is None or value.strip() in ("", "???"):
            continue
        value = clean(value)
        if drop_first:
            value = "\n".join(value.split("\n")[1:])
        if value:
            row[code] = value
    if row:
        out[key] = row
    else:
        missing.append(source)

for node, name in NODES.items():
    put(f"desc.{node}", f"String_UI_desc_{name}_body", drop_first=node not in ("str", "dex", "agi", "wis", "int", "con"))
for stat in STATS:
    put(f"stat.{stat}", f"String_StatName_{stat}_body")
    put(f"tip.{stat}", f"String_UI_desc_{stat}") if f"String_UI_desc_{stat}" in texts["de"] else put(f"tip.{stat}", f"String_UI_desc_{stat}_body")
for group in GROUPS:
    put(f"grp.{group}", f"String_UI_CHARACTERSTATUS_SUBTITLE_{group}_body")

json.dump(out, open(sys.argv[2], "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"), sort_keys=True)
print(len(out), "keys; missing:", missing)
