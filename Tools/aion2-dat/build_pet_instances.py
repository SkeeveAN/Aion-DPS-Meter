"""Writes Client/assets/aion2/pets/instances.json: a display name (every language) for each map other than the three world maps on which pet monsters
spawn, so the "Rare" tab of the pet settings can say "Blue Breath Isle" instead of a file path.

Usage: build_pet_instances.py <spawns.json> <instances.json of the backend> <l10n dir> <out.json>

The name of a map file comes from the client's own text `String_STR_Map_<map name>_body` (the map name is the last folder, without the difficulty suffix
Easy/Normal/Hard/Hell/Extreme/Despair); the party dungeons that have no such text are matched to the backend's instance table by their folder name.
Maps without any name keep an empty entry (the client then shows the file name). The difficulties of one dungeon share a name; the client counts a
dungeon once, by its largest difficulty, not summed.
"""
import json
import os
import re
import sys

WORLD = ("World/World_L/World_L_A", "World/World_D/World_D_A", "Intersever/Abyss/Abyss_Reshanta_A")
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
DIFFICULTY = re.compile(r"_(Easy|Normal|Hard|Hell|Extreme|Despair)$")


def main():
    spawns_path, backend_path, l10n_dir, out_path = sys.argv[1:5]
    maps = json.load(open(spawns_path))["maps"]
    l10n = {lang: json.load(open(os.path.join(l10n_dir, f"l10n_{code}.json"), encoding="utf-8")) for lang, code in LANGS.items()}
    table = {i["key"].split(":")[0].lower(): i["name"] for i in json.load(open(backend_path, encoding="utf-8"))["items"]}
    out = {}
    for map_name in maps:
        if map_name in WORLD or "/InstanceLayer/" in map_name:
            continue
        parts = map_name.split("/")
        seg = parts[-1]
        base = DIFFICULTY.sub("", seg)
        names = None
        for cand in (seg, base):
            key = f"String_STR_Map_{cand}_body"
            if key in l10n["en"]:
                names = {lang: t[key] for lang, t in l10n.items() if key in t}
                break
        if names is None:
            folder = parts[-2] if len(parts) > 1 else ""
            for cand in (folder, base, base.split("_")[0], folder.split("_")[0]):
                hit = table.get(cand.lower()) or next((n for k, n in table.items() if len(cand) > 3 and cand.lower().startswith(k)), None)
                if hit:
                    names = {lang: hit[lang] for lang in LANGS if lang in hit}
                    break
        out[map_name] = names or {}
    json.dump({"maps": out}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print(len(out), "maps,", sum(1 for v in out.values() if v), "named")


if __name__ == "__main__":
    main()
