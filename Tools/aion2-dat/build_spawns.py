"""Reads the spawn points out of the client's per-map files (AION2/Content/Data/Map/**/MapData.dat) and writes the ones of pet monsters.

Usage:  python3 build_spawns.py <dir with the unpacked AION2/Content/Data/Map> <pets.json> <out.json>

A MapData.dat is not the table container of the other .dat files: the whole file is XOR-ed with the four bytes 25 a8 7e 91 (the same
bytes that hide the strings in the tables). Decoded it starts with a comma separated list of numbers and then holds the unreal
properties as text-named entries: one spawn group per point, `<group name>`, `NpcIdList` (u64 ids), `EnvObjIdList` and `Positions`
(MapData_Position: x, y, z as the upper five bytes of a little-endian double, then 3 zero bytes, in the game's world units - the same
units as the position in the spawn frame 0x4136). Checked against 6,151 spawns seen in recordings: the median distance to the nearest
map point is 78 units (under a metre), 10 % are exact.
"""
import json
import os
import re
import struct
import sys

KEY = bytes([0x25, 0xA8, 0x7E, 0x91])


def dbl(u):
    return struct.unpack("<d", struct.pack("<Q", (u & 0xFFFFFFFFFF) << 24))[0]


def parse(path):
    raw = open(path, "rb").read()
    p = bytes(raw[i] ^ KEY[i % 4] for i in range(len(raw)))
    groups = []
    for m in re.finditer(rb"\x01\x00\x00\x00\x0a\x00\x00\x00NpcIdList\x00", p):
        s = m.start()
        for length in range(2, 140):
            a = s - length
            if a >= 4 and p[s - 1] == 0 and struct.unpack_from("<i", p, a - 4)[0] == length and all(32 <= c < 127 for c in p[a:s - 1]):
                groups.append((a - 4, p[a:s - 1].decode(), m.end()))
                break
    for index, (_, name, start) in enumerate(groups):
        end = groups[index + 1][0] if index + 1 < len(groups) else len(p)
        seg = p[start:end]
        i = seg.find(b"TableItemId\x00")
        j = seg.find(b"EnvObjIdList")
        ids = []
        if 0 <= i < j:
            body = seg[i + 12 + 17:j - 4]
            ids = [v for v in (struct.unpack_from("<Q", body, q)[0] for q in range(0, len(body) - 7, 8)) if v]
        pi = seg.find(b"MapData_Position\x00")
        if pi >= 0 and len(seg) >= pi + 17 + 20 + 24:
            d = seg[pi + 17 + 20:]
            yield name, ids, [dbl(struct.unpack_from("<Q", d, 8 * n)[0]) for n in range(3)]


def main():
    root, pets_path, out_path = sys.argv[1:4]
    monsters = {int(k): v for k, v in json.load(open(pets_path))["monsters"].items()}
    maps = {}
    total = 0
    for folder, _, files in os.walk(root):
        if "MapData.dat" not in files:
            continue
        name = os.path.relpath(folder, root).replace(os.sep, "/")
        per_npc = {}
        for _, ids, pos in parse(os.path.join(folder, "MapData.dat")):
            for npc in ids:
                if npc in monsters and all(abs(c) < 1e7 for c in pos):
                    per_npc.setdefault(str(npc), []).append([round(c) for c in pos])
                    total += 1
        if per_npc:
            maps[name] = per_npc
    json.dump({"maps": maps}, open(out_path, "w"), separators=(",", ":"))
    print(f"{len(maps)} maps with pet monsters, {total} spawn points -> {out_path} ({os.path.getsize(out_path) // 1024} KB)")


if __name__ == "__main__":
    main()
