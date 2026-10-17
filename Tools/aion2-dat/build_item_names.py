"""Builds Client/assets/aion2/items/item_names.json: the name of every item in the eight languages of the client, and its grade.

Usage:  python3 build_item_names.py <Item.bin> <l10n dir> <out.json>

Item.bin is the decrypted `Item` table (dectable.py, from pakchunk401000-Windows_0_P.pak), the l10n dir the output of extract_l10n.py.
A row is `[u32 item id][internal name FString][STR_ITEM_<KEY> FString]...[EItemGrade::<grade> FString]...`; the text of the name is
`String_<STR_ITEM_KEY>_body` in `L10NString.dat` (checked on 2026-10-17: "Ludra's Blade of Extinction"). Out: `{"<id>": [grade, en, de, fr, es, ru, pt, ja, ko]}`;
a language whose text equals the English one is left as "" to keep the file small. Grade: the index of GRADES (the scale of the character window, 0 = none).
"""
import json
import os
import struct
import sys
from build_pets import strings

LANGS = ["en-US", "de-DE", "fr-FR", "es-ES", "ru-RU", "pt-BR", "ja-JP", "ko-KR"]
GRADES = ["", "Common", "Rare", "Legend", "Unique", "Epic", "Mythic", "Special"]  # the scale of the character window


def main():
    item_path, l10n_dir, out_path = sys.argv[1:4]
    table = open(item_path, "rb").read()
    tables = [json.load(open(os.path.join(l10n_dir, f"l10n_{loc}.json"), encoding="utf-8")) for loc in LANGS]
    found = strings(table)
    out = {}
    for index, (offset, text) in enumerate(found):
        if not text.startswith("STR_ITEM_") or text.endswith("_DESC") or index == 0:
            continue
        item_id = struct.unpack_from("<I", table, found[index - 1][0] - 4)[0]
        if item_id < 1000:
            continue
        key = f"String_{text}_body"
        names = [t.get(key, "") for t in tables]
        if not names[0]:
            continue
        grade = 0
        for _, s in found[index + 1:index + 14]:
            if s.startswith("STR_ITEM_") and not s.endswith("_DESC"):
                break
            if s.startswith("EItemGrade::"):
                label = s.split("::", 1)[1]
                grade = GRADES.index(label) if label in GRADES else -1
                break
        out[str(item_id)] = [grade, names[0]] + ["" if n == names[0] else n for n in names[1:]]
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, separators=(",", ":"))
    labels = {}
    for v in out.values():
        labels[v[0]] = labels.get(v[0], 0) + 1
    print(len(out), "items; grades", labels)


main()
