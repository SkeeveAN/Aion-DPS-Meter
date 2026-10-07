"""Builds Backend/src/data/aion2/titles.json (title id -> names in every client language, grade) from the
decrypted Title table and the l10n JSON files made by extract_l10n.py.

Usage:  python3 build_titles.py <Title.bin from dectable.py> <dir with l10n_<locale>.json> <out.json>

A Title row is [i32 id][name FString "L_SealDungeon_050"][text key "Title_<name>_desc"]...; the grade is the row's
"ETitleGrade::<Grade>" enum string, the display name is the l10n value of the text key. Strings are UTF-16 with the
8 byte XOR used by the data tables (see README.md).
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

strings = []  # (offset, text)
p = 516
while p < len(body) - 8:
    n = -struct.unpack_from("<i", body, p)[0]
    if 3 < n < 120 and p + 4 + 2 * n <= len(body):
        raw = body[p + 4:p + 4 + 2 * n]
        s = bytes(c ^ XOR[i % 8] for i, c in enumerate(raw)).decode("utf-16le", "replace")[:-1]
        if re.fullmatch(r"[A-Za-z0-9_:]+", s):
            strings.append((p, s))
            p += 4 + 2 * n
            continue
    p += 1

rows = {}
for i, (pos, s) in enumerate(strings):
    if re.fullmatch(r"[LD]_[A-Za-z0-9_]+", s) and i + 1 < len(strings) and strings[i + 1][1] == f"Title_{s}_desc":
        title_id = struct.unpack_from("<I", body, pos - 4)[0]
        grade = None
        for _, t in strings[i + 2:i + 40]:
            if t.startswith("ETitleGrade::"):
                grade = t.split("::")[1]
                break
            if re.fullmatch(r"[LD]_[A-Za-z0-9_]+", t):
                break
        names = {code: texts[code].get(f"Title_{s}_desc") for code in locales}
        names = {k: v for k, v in names.items() if v and v != "???"}
        if names and 1_000_000 <= title_id <= 99_999_999:
            rows[str(title_id)] = {"n": names, "g": grade}

json.dump(rows, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"), sort_keys=True)
print(len(rows), "titles")
