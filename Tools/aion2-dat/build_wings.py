"""Builds Backend/src/data/aion2/wings.json from the decrypted Wing table (dectable.py, Wing.dat from pakchunk401000-Windows_0_P.pak).
Usage: python3 build_wings.py <Wing.bin> <l10n dir with l10n_<locale>.json> <out json>
A row is [u32 itemId][internal name Wing_<L|D>_<model>][name key Wing_Wing_<L|D>_<model>_desc]...[Icon_<model>]; the L10N value of the
"_desc" key is the wing's name. 30xxxxxx = Light (Elyos) wings, 40xxxxxx = Dark (Asmodian) twins of the same model."""
import json, os, re, struct, sys, importlib.util
here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("bp", os.path.join(here, "build_pets.py")); bp = importlib.util.module_from_spec(spec); spec.loader.exec_module(bp)
LOC = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
d = open(sys.argv[1], "rb").read(); s = bp.strings(d)
T = {c: json.load(open(os.path.join(sys.argv[2], f"l10n_{l}.json"), encoding="utf-8")) for c, l in LOC.items()}
out = {}
for i, (o, t) in enumerate(s):
    if i + 1 < len(s) and re.match(r"Wing_[LD]_", t) and s[i + 1][1].startswith("Wing_Wing_") and s[i + 1][1].endswith("_desc"):
        names = {c: T[c][s[i + 1][1]] for c in T if s[i + 1][1] in T[c]}
        icon = next((x[1] for x in s[i + 2:i + 8] if x[1].startswith("Icon_")), None)
        if "en" in names:
            out[str(struct.unpack_from("<I", d, o - 4)[0])] = {"names": names, "icon": icon}
json.dump(out, open(sys.argv[3], "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
print(len(out), "wings")
