"""Adds the monster/item/misc skills of the client's text tables to skill_names_i18n.json (player skills stay as they are).

Usage:  python3 build_skill_i18n.py <l10n dir from extract_l10n.py> <skill_names_i18n.json>

Keys: SkillString_STR_SKILL_PC_<CLASS>_<id>_skill_name (players), SkillString_SkillString_<NPC|ETC|ITEM|Polymorph|...>_<id>_skill_name.
A boss skill's English name in skill_names.json is mostly just "Attack"; the client's text has the real one ("Shuriken Toss").
"""
import json
import os
import re
import sys

locales = {"de": "de-DE", "en": "en-US", "es": "es-ES", "fr": "fr-FR", "ja": "ja-JP", "ko": "ko-KR", "pt": "pt-BR", "ru": "ru-RU"}
texts = {code: json.load(open(os.path.join(sys.argv[1], f"l10n_{loc}.json"), encoding="utf-8")) for code, loc in locales.items()}
target = sys.argv[2]
table = json.load(open(target, encoding="utf-8"))
pattern = re.compile(r"^SkillString_(?:SkillString_)?(?:STR_SKILL_PC_[A-Z]+|NPC|ETC|ITEM|Polymorph|GuardianGeneral|FE_Polymorph)_(\d+)_skill_name$")
added = 0
for key in texts["en"]:
    m = pattern.match(key)
    if not m or m.group(1) in table:
        continue
    names = {code: t[key].strip() for code, t in texts.items() if t.get(key, "").strip() and t[key].strip() != "???"}
    if names:
        table[m.group(1)] = dict(sorted(names.items()))
        added += 1
with open(target, "w", encoding="utf-8") as f:
    json.dump(dict(sorted(table.items(), key=lambda kv: int(kv[0]))), f, ensure_ascii=False, separators=(",", ":"))
print(f"added {added}, total {len(table)}")
