"""Builds the list of the field bosses (the named monsters the in-game map lists under "Field monsters") for the world boss page.

Usage:  python3 build_field_bosses.py <NpcData.bin> <WorldMapFieldNamed.bin> <npc_names.json> <l10n dir> <unpacked AION2/Content/Data/Map> <out.json>

- WorldMapFieldNamed lists the bosses by their internal NPC name and says how they come back: "Basic" (after a kill, the server keeps the
  time) or "PeriodSpawn" (a fixed time, see PeriodSpawn.dat). NpcData turns the internal name into the NPC id (same row layout as build_npcs.py).
- The map files (MapData.dat, XOR 25 a8 7e 91 like build_spawns.py) say on which map a boss stands and where.
- The in-game list of a map (opcode 01 91) numbers its bosses 1..n in the order of their NPC ids; the map number is `id` below (1010 Verteron,
  1110 Altgard - seen in recordings; the others are learnt by the client from the positions, see Aion2FieldBosses).
- The fixed times of the Abyss bosses come from PeriodSpawn.dat (time of day in units of 140,625,000 = 1 hour, cycle in minutes) and the
  weekdays the community site aion2.dev reads out of the same table; the days could not be read out of the table by this tool.
"""
import json
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_spawns as bs
from build_pets import strings

LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}

# map file -> (list number seen in recordings or 0, l10n key of the name)
MAPS = [
    ("World/World_L/World_L_A", 1010),
    ("World/World_L/World_L_B", 0),
    ("World/World_D/World_D_A", 1110),
    ("World/World_D/World_D_B", 0),
    ("Intersever/Abyss/Abyss_Reshanta_A", 0),
    ("Intersever/Abyss/Abyss_Reshanta_C", 0),
]

CYCLE = {"kind": "cycle", "base": "01:00", "everyMinutes": 180, "openMinutes": 20}
NAHMA = {"kind": "weekly", "days": [0, 5], "at": "21:00", "openMinutes": 20}
NAMED = {"kind": "weekly", "days": [1, 4, 6], "at": "21:30", "openMinutes": 20}
FIXED = {2600089: CYCLE, 2600084: NAHMA, 2600093: NAHMA, 2600094: NAHMA, 2600479: NAHMA, 2600480: NAHMA,
         2600096: NAMED, 2600097: NAMED, 2600098: NAMED, 2600520: NAMED, 2600521: NAMED, 2600522: NAMED}


def internal_names(path):
    """internal NPC name -> NPC id out of the decrypted NpcData table."""
    d = open(path, "rb").read()
    key = bytes([0x25, 0, 0xA8, 0, 0x7E, 0, 0x91, 0])
    marker = bytes([0x76, 0, 0xFC, 0, 0x2C, 0, 0xCE, 0])

    def dec(p, n):
        raw = d[p:p + 2 * (n - 1)]
        return bytes(b ^ key[j % 8] for j, b in enumerate(raw)).decode("utf-16le", "replace")

    result = {}
    pos = 0
    while True:
        q = d.find(marker, pos)
        if q < 0:
            break
        pos = q + 1
        if q < 12:
            continue
        len2 = struct.unpack_from("<i", d, q - 4)[0]
        if not (-120 < len2 < -6):
            continue
        key2 = dec(q, -len2)
        if not key2.startswith("STR_") or not key2.isascii():
            continue
        for l1 in range(2, 100):
            p1 = q - 4 - 2 * l1 - 4
            if p1 < 4:
                break
            if struct.unpack_from("<i", d, p1)[0] != -l1:
                continue
            n1 = dec(p1 + 4, l1)
            if n1.isascii() and n1.isprintable():
                npc = struct.unpack_from("<i", d, p1 - 4)[0]
                if 100000 <= npc < 100000000:
                    result[n1] = npc
                break
    return result


def field_named(path):
    """[(internal name, spawn type)] of WorldMapFieldNamed."""
    ss = strings(open(path, "rb").read())
    return [(ss[i - 1][1], s.split("::")[1]) for i, (_, s) in enumerate(ss) if s.startswith("EWorldMapFieldNamedSpawnType::")]


def spawn_points(map_root, wanted):
    """map name -> npc id -> (x, y) for the wanted NPC ids."""
    result = {}
    for name, _ in MAPS:
        raw = open(os.path.join(map_root, name, "MapData.dat"), "rb").read()
        p = bytes(raw[i] ^ bs.KEY[i % 4] for i in range(len(raw)))
        found = {}
        for m in re.finditer(rb"NpcIdList\x00", p):
            seg = p[m.end():m.end() + 400]
            ti, pm, pi = seg.find(b"TableItemId\x00"), seg.find(b"Positions\x00"), seg.find(b"MapData_Position\x00")
            if ti < 0 or pm < 0 or pi < 0 or len(seg) < pi + 17 + 20 + 24:
                continue
            body = seg[ti + 12 + 17:pm - 8]
            ids = [v for v in (struct.unpack_from("<Q", body, q)[0] for q in range(0, len(body) - 7, 8)) if v]
            d = seg[pi + 17 + 20:]
            x, y = (bs.dbl(struct.unpack_from("<Q", d, 8 * n)[0]) for n in range(2))
            for i in ids:
                if i in wanted:
                    found.setdefault(i, (round(x), round(y)))
        result[name] = found
    return result


def main():
    npc_file, named_file, names_file, l10n_dir, map_root, out_path = sys.argv[1:7]
    internal = internal_names(npc_file)
    npc_names = json.load(open(names_file, encoding="utf-8"))
    rows = [(internal.get(key), key, kind) for key, kind in field_named(named_file)]
    ids = {npc for npc, _, _ in rows if npc}
    points = spawn_points(map_root, ids)
    l10n = {code: json.load(open(os.path.join(l10n_dir, f"l10n_{file}.json"), encoding="utf-8")) for code, file in LANGS.items()}
    maps = []
    for name, number in MAPS:
        bosses = []
        for npc, key, kind in sorted(rows, key=lambda r: r[0] or 0):
            if npc in points[name]:
                x, y = points[name][npc]
                names = {c: npc_names.get(str(npc), {}).get(c) for c in LANGS}
                boss = {"npc": npc, "x": x, "y": y, "names": {c: t for c, t in names.items() if t}}
                if npc in FIXED:
                    boss["fixed"] = FIXED[npc]
                bosses.append(boss)
        title = {c: t.get(f"String_STR_Map_{name.rsplit('/', 1)[1]}_body") for c, t in l10n.items()}
        maps.append({"key": name.rsplit("/", 1)[1], "id": number, "names": {c: v for c, v in title.items() if v}, "bosses": bosses})
        print(name, number, len(bosses))
    json.dump({"maps": maps}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("wrote", out_path)


if __name__ == "__main__":
    main()
