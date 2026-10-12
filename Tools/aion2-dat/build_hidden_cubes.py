"""Writes Client/assets/aion2/maps/hidden_cubes.json: the possible places of the "Hidden Cube" (Versteckter Kubus) on the world maps.

Usage: build_hidden_cubes.py <unpacked AION2/Content/Data/Map> <EnvObjData.bin> <l10n dir> <out.json>

A cube is not a fixed object: the MapData.dat of a world map holds spawn groups named `EnvObj_<Region>_HiddenCube_<n>` (Elyos side, Verteron) or
`EnvObj_<Region>_HiddenBox_...` (Asmodian side, Altgard); each one lists the places where its cube may appear (`Positions`, one entry per place:
a location x, y, z followed by a rotation, both as five-byte doubles like in build_spawns.py) and the env object id of the cube (`EnvObjIdList`,
all of them are named "Hidden Cube" in the client). Which place is used right now is decided by the server, so the file holds every candidate.
"""
import json
import os
import re
import struct
import sys

import build_spawns as bs
from build_pets import strings

LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
WORLD = {"verteron": "World/World_L/World_L_A", "altgard": "World/World_D/World_D_A"}
GROUP = re.compile(rb"EnvObj_[A-Za-z0-9_]*(?:HiddenCube|HiddenBox)[A-Za-z0-9_]*")
NONE = b"\x05\x00\x00\x00None\x00"


def env_keys(table):
    """env id -> internal key (`E_HiddenCube_01_V04_001`) for every row of EnvObjData."""
    b = open(table, "rb").read()
    out = {}
    for offset, s in strings(b):
        if s[:2] == "E_" and not s.startswith(("EnvObjData_", "EEnv")):
            out[struct.unpack_from("<i", b, offset - 4)[0]] = s
    return out


def groups(path, cubes):
    """Yield (group name, env id, [(x, y, z), ...]) for every cube spawn group of a MapData.dat."""
    raw = open(path, "rb").read()
    p = bytes(raw[i] ^ bs.KEY[i % 4] for i in range(len(raw)))
    for m in GROUP.finditer(p):
        seg = p[m.start():p.find(NONE, m.start())]
        ti = seg.find(b"TableItemId\x00", seg.find(b"EnvObjIdList"))
        pm = seg.find(b"Positions\x00")
        pi = seg.find(b"MapData_Position\x00")
        if len(seg) > 4000 or ti < 0 or pm < 0 or pi < 0:
            continue
        body = seg[ti + 12 + 17:pm - 8]
        ids = [v for v in (struct.unpack_from("<I", body, q)[0] for q in range(0, min(len(body), 40) - 3, 4)) if v in cubes]
        size = struct.unpack_from("<Q", seg, pi - 12)[0]
        data = seg[pi + 17 + 20:]
        points = []
        for n in range(size // 48):          # 6 slots per entry: location x y z, rotation x y z
            if 48 * n + 24 > len(data):
                break
            pos = [bs.dbl(struct.unpack_from("<Q", data, 48 * n + 8 * k)[0]) for k in range(3)]
            if all(abs(c) < 1e7 for c in pos) and (pos[0] or pos[1]):
                points.append(tuple(round(c) for c in pos))
        if ids and points:
            yield m.group().decode(), ids[0], points


def build(map_dir, table, l10n):
    """-> (names per language, {map key: [{"group", "id", "points"}]})"""
    keys = env_keys(table)
    # only the objects the client calls "Hidden Cube" (the dungeon treasure boxes have other names)
    cubes = {i: k for i, k in keys.items() if l10n["en"].get(f"EnvObjData_{k}_desc") == "Hidden Cube"}
    names = {lang: t["EnvObjData_" + next(iter(cubes.values())) + "_desc"] for lang, t in l10n.items()}
    out = {}
    for map_key, map_name in WORLD.items():
        seen, rows = set(), []
        for name, env_id, points in groups(os.path.join(map_dir, map_name, "MapData.dat"), cubes):
            unique = [pt for pt in dict.fromkeys(points)]
            if (name, tuple(unique)) in seen:
                continue
            seen.add((name, tuple(unique)))
            rows.append({"group": name, "id": env_id, "points": [[pt[0], pt[1], pt[2]] for pt in unique]})
        out[map_key] = rows
        print(map_key, len(rows), "groups,", sum(len(r["points"]) for r in rows), "places")
    return names, out


def main():
    map_dir, table, l10n_dir, out_path = sys.argv[1:5]
    l10n = {lang: json.load(open(os.path.join(l10n_dir, f"l10n_{code}.json"), encoding="utf-8")) for lang, code in LANGS.items()}
    names, out = build(map_dir, table, l10n)
    json.dump({"names": names, "maps": out}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))


if __name__ == "__main__":
    main()
