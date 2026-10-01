"""Decrypts the text tables of an installed AION 2 client into one JSON file per language.

Usage:  python3 extract_l10n.py <dir with unpacked game files> <out dir>

<dir> must hold  Aion2/Content/StartUpData/Table/key_manifest.dat  and
AION2/Content/L10N/Text/<locale>/L10NString.dat  (see README.md for unpacking them out of the paks).
The two format constants come from constants.local.json next to this file (not in the repository).
"""
import json
import os
import sys

from aion2dat import Aion2Dat

here = os.path.dirname(os.path.abspath(__file__))
consts = json.load(open(os.path.join(here, "constants.local.json")))
dat = Aion2Dat(bytes.fromhex(consts["manifest_hash_key_hex"]), int(consts["header_xor_constant"], 16))

root, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
count = dat.load_manifest(open(os.path.join(root, "Aion2/Content/StartUpData/Table/key_manifest.dat"), "rb").read())
print(f"key manifest: {count} keys")
text_dir = os.path.join(root, "AION2/Content/L10N/Text")
for locale in sorted(os.listdir(text_dir)):
    path = os.path.join(text_dir, locale, "L10NString.dat")
    if not os.path.exists(path):
        continue
    info, namespace, entries = dat.read_l10n(open(path, "rb").read(), locale)
    with open(os.path.join(out, f"l10n_{locale}.json"), "w", encoding="utf-8") as f:
        json.dump(entries, f, ensure_ascii=False)
    print(f"{locale}: {len(entries)} strings")
