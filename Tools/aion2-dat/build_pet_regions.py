"""Writes Client/assets/aion2/pets/regions.json: the named regions of the three world maps (Verteron, Altgard, Abyss) and the pets that
have a spawn point in each, for the "Region" tab of the pet settings.

Usage: build_pet_regions.py <Subzone.bin> <WorldMapUIRegion.bin> <unpacked AION2/Content/Data/Map> <l10n dir> <pets.json> <spawns.json> <out.json>
(the two tables decrypted with dectable.py, the map folder and l10n as for build_spawns.py / extract_l10n.py)

How it works (2026-10-10): every MapData.dat holds the area volumes as text-named `Points` (Vector2D, x and y as the upper five bytes of a
little-endian double, 16 bytes per point) followed by the volume's name `<Name>_Subzone`. `Subzone.dat` pairs that name with a text key
`STR_Subzone_<Name>`; `WorldMapUIRegion.dat` lists the keys the world map shows as regions (`Region_Rank1`; `Region_Rank2` are the small
places: camps, outposts). A spawn point belongs to the smallest Rank1 area around it, else the smallest Rank2 area, else the smallest
named area at all. The region's name is `String_<key>_body` in every language.
"""
import collections
import json
import os
import re
import struct
import sys

XOR = bytes([0x25, 0x00, 0xA8, 0x00, 0x7E, 0x00, 0x91, 0x00])
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU"}
MAPS = {
    "verteron": ["World/World_L/World_L_A"],
    "altgard": ["World/World_D/World_D_A"],
    "abyss": ["Intersever/Abyss/Abyss_Reshanta_A", "Intersever/Abyss/Abyss_Reshanta_C", "Intersever/Abyss/Abyss_Reshanta_D"],
}


def strings(b):
    """FStrings of a data table: i32 negative length, UTF-16 XOR-ed from the start of each string."""
    i, out = 0, []
    while i < len(b) - 4:
        n = struct.unpack_from("<i", b, i)[0]
        if -6000 < n < -1:
            raw = b[i + 4:i + 4 - 2 * n]
            if len(raw) == -2 * n:
                try:
                    t = bytes(c ^ XOR[k % 8] for k, c in enumerate(raw)).decode("utf-16-le")
                    if all(32 <= ord(c) < 0x3000 for c in t.rstrip("\0")) and len(t) > 1:
                        out.append(re.sub(r"[^A-Za-z0-9_:]+$", "", t))
                        i += 4 + 2 * -n
                        continue
                except UnicodeDecodeError:
                    pass
        i += 1
    return out


def dbl(raw):
    return struct.unpack("<d", raw)[0]


def polygons(path):
    """{volume name: [(x, y), ...]} of one MapData.dat (the whole file is XOR-ed with 25 a8 7e 91)."""
    key = (0x25, 0xA8, 0x7E, 0x91)
    d = bytes(c ^ key[i % 4] for i, c in enumerate(open(path, "rb").read()))
    out = {}
    for m in re.finditer(rb"Points\x00", d):
        j = d.find(b"Vector2D\x00", m.start(), m.start() + 80)
        if j < 0:
            continue
        best, end = [], 0
        for o in range(48):
            pts, p = [], j + 9 + o
            while p + 16 <= len(d):
                x, y = dbl(d[p:p + 8]), dbl(d[p + 8:p + 16])
                if not (abs(x) < 6e5 and abs(y) < 6e5 and x and y and d[p:p + 3] == b"\0\0\0" and d[p + 8:p + 11] == b"\0\0\0"):
                    break
                pts.append((x, y))
                p += 16
            if len(pts) > len(best):
                best, end = pts, p
        name = re.search(rb"([A-Za-z0-9_]*[Ss]ubzone[A-Za-z0-9_]*)\x00", d[end:end + 400])
        if name and len(best) >= 3:
            out[name.group(1).decode()] = best
    return out


def inside(x, y, poly):
    c = False
    for (x1, y1), (x2, y2) in zip(poly, poly[1:] + poly[:1]):
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            c = not c
    return c


def area(p):
    return abs(sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(p, p[1:] + p[:1]))) / 2


EVENTS = "_events"  # pets that only spawn in the event and quest phases of the maps
EVENT_NAMES = {"en": "Events", "de": "Events", "fr": "Événements", "es": "Eventos", "ru": "События"}
OTHER = "_other"  # pets that spawn outside every named area; the client names it


def main():
    sub_path, ui_path, map_dir, l10n_dir, pets_path, spawns_path, out_path = sys.argv[1:8]
    rows, cur = [], None
    for t in strings(open(ui_path, "rb").read())[1:]:
        if t.startswith("STR_") and not t.endswith("_Desc"):
            cur = [t]
            rows.append(cur)
        elif cur is not None:
            cur.append(t)
    rank1 = [r[0] for r in rows if "EWorldMapInfoType::Region_Rank1" in r]
    rank2 = [r[0] for r in rows if "EWorldMapInfoType::Region_Rank2" in r]
    sub = strings(open(sub_path, "rb").read())
    str2sub = collections.defaultdict(set)
    for i, t in enumerate(sub):
        if t.startswith("STR_") and i > 0 and "ubzone" in sub[i - 1]:
            str2sub[t].add(sub[i - 1])
    l10n = {lang: json.load(open(os.path.join(l10n_dir, f"l10n_{loc}.json"), encoding="utf-8")) for lang, loc in LANGS.items()}

    def names(key):
        n = {lang: l10n[lang].get(f"String_{key}_body") for lang in LANGS}
        return {lang: v or n["en"] for lang, v in n.items()} if n["en"] else None

    pets = json.load(open(pets_path, encoding="utf-8"))
    monsters = pets["monsters"]
    known = {int(k) for k in pets["pets"]}
    spawns = json.load(open(spawns_path))["maps"]
    others = [k for k in str2sub if k not in rank1 and k not in rank2 and names(k)]
    groups = []
    for gid, maps in MAPS.items():
        found = collections.defaultdict(set)
        layer_pets = set()
        for map_name in maps:
            poly = polygons(os.path.join(map_dir, map_name, "MapData.dat"))
            # on the open world maps a box 20 times the size of the next volume is a catch-all that covers the whole map (Verteron: "Destroyed Abyss Gate", Altgard: "Amunta's Hideout"), not a region; the Abyss maps are one named zone each (Black Fragments), which stays
            sizes = sorted(area(p) for p in poly.values())
            catch_all = {n for n, p in poly.items() if map_name.startswith("World/") and len(sizes) > 1 and area(p) > 20 * sizes[-2]}
            poly = {n: p for n, p in poly.items() if n not in catch_all}
            if catch_all:
                print(map_name, "catch-all volume left out:", ", ".join(sorted(catch_all)))
            tiers = [[(k, area(poly[sn]), poly[sn]) for k in ks for sn in str2sub.get(k, ()) if sn in poly] for ks in (rank1, rank2, others)]
            for key, npcs in spawns.items():
                if key.startswith(map_name + "/InstanceLayer"):
                    # event and quest phases of the same map: not where a pet lives, but the only place of some pets
                    for npc in npcs:
                        ids = monsters.get(npc)
                        layer_pets.update((ids if isinstance(ids, list) else [ids]) if ids else ())
                    continue
                if key != map_name:
                    continue
                for npc, points in npcs.items():
                    ids = monsters.get(npc)
                    for x, y, _ in points if ids else ():
                        for tier in tiers:
                            hit = [(a, k) for k, a, p in tier if inside(x, y, p)]
                            if hit:
                                found[min(hit)[1]].update(ids if isinstance(ids, list) else [ids])
                                break
                        else:
                            found[OTHER].update(ids if isinstance(ids, list) else [ids])  # in no named area at all
        regions, other = [], None
        for key, ids in found.items():
            ids = sorted(i for i in ids if i in known)
            if ids and key == OTHER:
                other = {"key": OTHER, "names": {}, "pets": ids}
            elif ids and names(key):
                regions.append({"key": key, "names": names(key), "pets": ids})
        regions.sort(key=lambda r: r["names"]["en"].casefold())
        only_events = sorted(i for i in layer_pets if i in known and not any(i in r["pets"] for r in regions) and not (other and i in other["pets"]))
        if only_events:
            regions.append({"key": EVENTS, "names": EVENT_NAMES, "pets": only_events})
        if other:
            regions.append(other)
        groups.append({"id": gid, "regions": regions})
        print(gid, len(regions), "regions")
    json.dump({"groups": groups}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))


main()
