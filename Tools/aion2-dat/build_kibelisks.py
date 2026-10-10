"""Writes Client/assets/aion2/maps/kibelisks.json: the Kibelisks (the teleport artifacts: "Elyos-Kibelisk", one per named place) of the three world
maps, with their names in every language, for the "Rare" tab of the pet settings (the nearest one is named next to a spawn point).

Usage: build_kibelisks.py <EnvObjData.bin> <unpacked AION2/Content/Data/Map> <l10n dir> <out.json>

An `EnvObjData` row `[i32 id][key]` whose key contains `TeleportArtifact` is a Kibelisk (`E_L1_CantasValley_TeleportArtifact_001`); its name is the l10n
text `EnvObjData_<key>_desc` ("Westlicher Lagerplatz im Cantastal"). The MapData.dat files place them like gather sources (`EnvObjIdList` + `Positions`,
see build_gather.py). Player-built Kisks have no fixed place and are not in the files; the two Abyss ones are the "roots of Latesran".
"""
import json
import os
import struct
import sys

import build_gather as g
from build_pets import strings

WORLD = {"verteron": "World/World_L/World_L_A", "altgard": "World/World_D/World_D_A", "abyss": "Intersever/Abyss/Abyss_Reshanta_A"}
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}


def main():
    env_path, map_dir, l10n_dir, out_path = sys.argv[1:5]
    b = open(env_path, "rb").read()
    keys = {}
    for offset, s in strings(b):
        if "TeleportArtifact" in s and not s.startswith(("EnvObjData_", "TeleportArtifact_", "EEnv")):
            keys[struct.unpack_from("<i", b, offset - 4)[0]] = s
    l10n = {lang: json.load(open(os.path.join(l10n_dir, f"l10n_{code}.json"), encoding="utf-8")) for lang, code in LANGS.items()}
    out = {}
    for map_key, map_name in WORLD.items():
        seen, rows = set(), []
        for ids, pos in g.placements(os.path.join(map_dir, map_name, "MapData.dat")):
            for env_id in ids:
                key = keys.get(env_id)
                if not key or not all(abs(c) < 1e7 for c in pos):
                    continue
                names = {lang: t[f"EnvObjData_{key}_desc"] for lang, t in l10n.items() if f"EnvObjData_{key}_desc" in t}
                if names.get("en") and names["en"] not in seen:
                    seen.add(names["en"])
                    rows.append({"names": names, "x": round(pos[0]), "y": round(pos[1])})
        out[map_key] = rows
        print(map_key, len(rows), "Kibelisks")
    json.dump({"maps": out}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))


if __name__ == "__main__":
    main()
