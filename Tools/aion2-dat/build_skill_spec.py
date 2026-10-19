"""Writes the texts of the specialisation stones and stigma effects, one file per language, for the profile page's skill tooltips.

Usage:  python3 build_skill_spec.py <l10n dir from extract_l10n.py> <out dir> <SkillEffect.bin> <SkillAbnormalEffect.bin>
        (out: Web-Frontend/skill-spec; the two tables are the decrypted .dat files, see skill_effect_tables.py)

Source keys: SkillString_STR_SKILL_PC_<CLASS>_<skill id + 10 * n>_specialized_skill_desc, n = 1..5. For an active skill the five
texts are its five stones (the game shows them as green, blue, gold, orange, red); for a stigma n = 1..4 are the effects it
unlocks at level 5, 10, 15 and 20. Output per language: { "<base skill id>": [text1 .. text5] }.
Numbers in the client's texts are placeholders into the skill effect tables ({se:...}, {abe:...}); skill_effect_tables.py fills the
ones it can read (checked against the game's own tooltips: "+10% PvE Damage Boost", "-1 s cooldown", "Absorbs 1% HP"). A placeholder
it cannot resolve (damage numbers, target counts) is dropped together with its unit, so "Absorbs {..}% HP" would read "Absorbs HP".
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from skill_effect_tables import fill, load_effects

locales = {"de": "de-DE", "en": "en-US", "es": "es-ES", "fr": "fr-FR", "ja": "ja-JP", "ko": "ko-KR", "pt": "pt-BR", "ru": "ru-RU"}
pattern = re.compile(r"^SkillString_STR_SKILL_PC_[A-Z]+_(\d+)_specialized_skill_desc$")
placeholder = re.compile(r"[+\-−]?\{[^}]*\}\s*(?:%|Sek\.|sec\.?|s\b|초|秒|с\b)?")


def clean(text: str) -> str:
    text = re.sub(r"<[^>]*>", "", text)
    text = placeholder.sub("", text)
    return re.sub(r"\s{2,}", " ", text).strip(" :")


os.makedirs(sys.argv[2], exist_ok=True)
se_rows, abe_rows = load_effects(sys.argv[3], sys.argv[4])
print(f"effect rows: {len(se_rows)} skill effects, {len(abe_rows)} abnormal effects")
for code, locale in locales.items():
    texts = json.load(open(os.path.join(sys.argv[1], f"l10n_{locale}.json"), encoding="utf-8"))
    out: dict[str, list[str]] = {}
    unresolved = 0
    total = 0
    for key, value in texts.items():
        m = pattern.match(key)
        if not m or not value.strip() or value.strip() == "???":
            continue
        skill = int(m.group(1))
        base, n = skill // 10000 * 10000, skill % 10000 // 10
        if skill % 10 != 0 or not 1 <= n <= 5 or skill % 10000 >= 60:
            continue
        slots = out.setdefault(str(base), ["", "", "", "", ""])
        filled, missing = fill(value, code, se_rows, abe_rows)
        total += value.count("{")
        unresolved += missing
        slots[n - 1] = clean(filled)
    path = os.path.join(sys.argv[2], f"{code}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(dict(sorted(out.items(), key=lambda kv: int(kv[0]))), f, ensure_ascii=False, separators=(",", ":"))
    print(code, len(out), os.path.getsize(path), f"placeholders {total}, unresolved {unresolved}")
