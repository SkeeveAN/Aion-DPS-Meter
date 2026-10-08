"""Reads the spawn points of gatherable objects (herbs, ores, wood, cotton, gems, Od, ...) out of the client's per-map files and writes them next
to the names of the objects.

Usage:  python3 build_gather.py <dir with the unpacked AION2/Content/Data/Map> <EnvObjData.bin> <l10n dir> <out.json>

EnvObjData.bin is the decrypted table (dectable.py). An object row is `[i32 id]['TableItemId'... key][desc key]...['EEnvObjectUsage::GatherSource']
['Gather_<Type>_lv<n>_...']`: the usage says that it is a gather source and the next name gives the kind. A MapData.dat (XOR 25 a8 7e 91, see
build_spawns.py) holds `EnvObjIdList` (u64 ids) followed by `Positions` (x, y, z) for every placement; the desc key `EnvObjData_<key>_desc` is the
name in the l10n files.
"""
import json
import os
import re
import struct
import sys

from build_pets import strings
import build_spawns as bs

# the kinds of the table -> what the client shows (OdCube and Source never stand on a world map)
CATEGORY = {"Od": "Od", "Herb": "Herb", "Food": "Food", "Ore": "Ore", "RareOre": "Ore", "Wood": "Wood", "Cotton": "Cotton",
            "Gemstone": "Gemstone", "Jewelry": "Gemstone", "Fragment": "Fragment"}
WORLD_MAPS = ("World/World_L/World_L_A", "World/World_D/World_D_A", "Intersever/Abyss/Abyss_Reshanta_A")
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU"}


def kind_of(key, usage, gather_name):
    """The kind of collectible: a gather source by what it gives (Od, Ore, Herb, Wood, Cotton, Gemstone, RareOre), the Od energy cubes, the
    fragments (the "traces" of a region) - or None for every other object."""
    if usage == "EEnvObjectUsage::GatherSource":
        m = re.match(r"Gather_Source_([A-Za-z]+)_", key)
        if m:
            return m.group(1)
        return gather_name.split("_")[1] if gather_name else None
    if usage == "EEnvObjectUsage::OdEnergyCube":
        return "OdCube"
    if "_fragment_" in key:
        return "Fragment"
    return None


def gatherables(table):
    """env id -> (key, kind) for every collectible row of the table."""
    b = open(table, "rb").read()
    ss = strings(b)
    out = {}
    for i, (_, s) in enumerate(ss):
        if s != "TableItemId" or i + 12 >= len(ss):
            continue
        key = ss[i + 1]
        usage = next((x for _, x in ss[i + 1:i + 12] if x.startswith("EEnvObjectUsage::")), "")
        gather = next((x for _, x in ss[i + 1:i + 12] if x.startswith("Gather_")), "")
        kind = kind_of(key[1], usage, gather)
        if kind:
            out[struct.unpack_from("<i", b, key[0] - 4)[0]] = (key[1], kind)
    return out


def placements(path):
    raw = open(path, "rb").read()
    p = bytes(raw[i] ^ bs.KEY[i % 4] for i in range(len(raw)))
    for m in re.finditer(rb"EnvObjIdList\x00", p):
        seg = p[m.end():m.end() + 400]
        ti = seg.find(b"TableItemId\x00")
        pm = seg.find(b"Positions\x00")
        pi = seg.find(b"MapData_Position\x00")
        if ti < 0 or pm < 0 or pi < 0 or len(seg) < pi + 17 + 20 + 24:
            continue
        body = seg[ti + 12 + 17:pm - 8]
        ids = [v for v in (struct.unpack_from("<Q", body, q)[0] for q in range(0, len(body) - 7, 8)) if v]
        d = seg[pi + 17 + 20:]
        yield ids, [bs.dbl(struct.unpack_from("<Q", d, 8 * n)[0]) for n in range(3)]


def main():
    root, table, l10n_dir, out_path = sys.argv[1:5]
    gather = gatherables(table)
    names = {}
    for code, file in LANGS.items():
        data = json.load(open(os.path.join(l10n_dir, f"l10n_{file}.json"), encoding="utf-8"))
        for env_id, (key, _) in gather.items():
            text = data.get(f"EnvObjData_{key}_desc")
            if text:
                names.setdefault(str(env_id), {})[code] = text
    maps = {}
    total = 0
    for folder, _, files in os.walk(root):
        if "MapData.dat" not in files:
            continue
        name = os.path.relpath(folder, root).replace(os.sep, "/")
        if name not in WORLD_MAPS:
            continue
        per = {}
        for ids, pos in placements(os.path.join(folder, "MapData.dat")):
            for env_id in ids:
                if env_id in gather and gather[env_id][1] in CATEGORY and all(abs(c) < 1e7 for c in pos):
                    per.setdefault(str(env_id), []).append([round(c) for c in pos])
                    total += 1
        if per:
            maps[name] = per
    used = {k for per in maps.values() for k in per}
    kinds = {k: {"kind": CATEGORY[gather[int(k)][1]], "names": names.get(k, {})} for k in sorted(used, key=int)}
    json.dump({"objects": kinds, "maps": maps}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
    print(f"{len(kinds)} gatherable objects, {len(maps)} maps, {total} placements -> {out_path} ({os.path.getsize(out_path) // 1024} KB)")


if __name__ == "__main__":
    main()
