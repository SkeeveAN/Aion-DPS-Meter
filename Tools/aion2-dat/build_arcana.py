"""Builds Client/assets/aion2/items/arcana.json: the experience each arcana grade needs per enchant level (decrypted
ArcanaEnchant table), the arcana sets (name and set effects, 8 client languages) from the l10n JSON files.

Usage:  python3 build_arcana.py <ArcanaEnchant.bin from dectable.py> <dir with l10n_<locale>.json> <out.json>

ArcanaEnchant rows: [FString "Arcana_Enchant_<Grade>"][u8 level][i32 exp to the next level]...; a grade's last level carries 0
(Unique: 3000 6000 12000 24000 48000 0 - the +2 and +3 caps 12.000 / 24.000 match the in-game window). Set effects: ItemSetEffect lists sets 001 and 002 with a 2 and a 4 piece effect each; the texts are
SkillAbnormalString_<40001011 ...> (set 001 = 40001011 for 2 pieces, 40002011 for 4 pieces, checked against the in-game
tooltip of a complete "Primal Vigor" set: +60 / +150 PvE Attack at HP >= 70 %). Only set 001 is taken: for the others the
mapping is not verified.
"""
import json
import os
import re
import struct
import sys

XOR = bytes.fromhex("2500a8007e009100")
body = open(sys.argv[1], "rb").read()
l10n_dir, out_path = sys.argv[2], sys.argv[3]
locales = {"de": "de-DE", "en": "en-US", "es": "es-ES", "fr": "fr-FR", "ja": "ja-JP", "ko": "ko-KR", "pt": "pt-BR", "ru": "ru-RU"}
texts = {code: json.load(open(os.path.join(l10n_dir, f"l10n_{loc}.json"), encoding="utf-8")) for code, loc in locales.items()}

GRADES = {"Common": 1, "Rare": 2, "Legend": 3, "Unique": 4}
caps = {}
for m in re.finditer(rb"[\x80-\xff]\xff\xff\xff", body):
    p = m.start()
    n = -struct.unpack_from("<i", body, p)[0]
    if not 10 < n < 40 or p + 4 + 2 * n > len(body):
        continue
    name = bytes(c ^ XOR[i % 8] for i, c in enumerate(body[p + 4:p + 4 + 2 * n])).decode("utf-16le", "replace")[:-1]
    g = re.fullmatch(r"Arcana_Enchant_(Common|Rare|Legend|Unique)", name)   # the Libra / S3 / S4 tables are other card types, not taken
    if g:
        q = p + 4 + 2 * n   # level u8, then the experience needed to reach the next level (i32), 0 on the last level
        caps.setdefault(str(GRADES[g.group(1)]), {})[body[q]] = struct.unpack_from("<i", body, q + 1)[0]
caps = {g: [v for _, v in sorted(r.items())] for g, r in caps.items()}

def name(code, key):
    return texts[code].get(key)

sets = {}
for number in ("001",):
    sets[number] = {
        "name": {c: name(c, f"String_STR_ARCANA_A_SET_{number}_body") for c in locales},
        "effects": [
            {"pieces": pieces, "text": {c: name(c, f"SkillAbnormalString_SkillAbnormalString_{key}_desc_effect") for c in locales}}
            for pieces, key in ((2, "40001011"), (4, "40002011"))
        ],
    }

json.dump({"expPerLevel": caps, "sets": sets}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(caps)
