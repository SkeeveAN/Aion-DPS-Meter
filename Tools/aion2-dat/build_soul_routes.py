"""Writes the places the client itself lists for every pet soul (the "Fundorte" page of the pet window), read from ItemGetRoute.

Usage: build_soul_routes.py <ItemGetRoute.bin> <Subzone.bin> <pets.json> <out.json>
(both tables decrypted with dectable.py; ItemGetRoute is in pakchunk401000-Windows_0_P.pak like the other data tables)

How it works (2026-10-11): a row is `[u32 item id][u32 n]["MonsterGetRoutes" property with n elements]...`. Every element holds, byte-aligned but NOT
4-byte aligned, five u32: group (0 for the first element, else 12xxxxx/13xxxxx), NPC id, map id (1010 Verteron, 1110 Altgard, 20 Abyss), zone id, 1..9.
The zone id is the row id of Subzone.dat (the u32 in front of the volume name), whose text key `STR_...` names the place. The element count n behind the item id is checked against what was found.
Output: {"<pet id>": [[map id, zone id, "STR_..." or null, npc id], ...]}
"""
import json
import re
import struct
import sys

XOR = bytes([0x25, 0x00, 0xA8, 0x00, 0x7E, 0x00, 0x91, 0x00])
GROUPS = {1010: "verteron", 1110: "altgard", 20: "abyss"}


def fstring_at(b, i):
    n = struct.unpack_from("<i", b, i)[0]
    if not -200 < n < -3 or i + 4 - 2 * n > len(b):
        return None
    try:
        t = bytes(c ^ XOR[k % 8] for k, c in enumerate(b[i + 4:i + 4 - 2 * n])).decode("utf-16-le").rstrip("\0")
    except UnicodeDecodeError:
        return None
    t = re.sub(r"[^A-Za-z0-9_]+$", "", t)
    return (t, i + 4 - 2 * n) if re.fullmatch(r"[A-Za-z0-9_]+", t) and len(t) > 3 else None


def zones(path):
    """{zone id: text key} of Subzone.dat: `[u32 id][volume name][STR_ key]`."""
    b = open(path, "rb").read()
    rows, i = [], 0
    while i < len(b) - 8:
        s = fstring_at(b, i)
        if s:
            rows.append((i, s[0]))
            i = s[1]
        else:
            i += 1
    out = {}
    for k, (pos, t) in enumerate(rows):
        if t.startswith("STR_") and k > 0:
            zid = struct.unpack_from("<I", b, rows[k - 1][0] - 4)[0]
            if 1 <= zid <= 99999 and zid not in out:
                out[zid] = t
    return out


def main():
    route_path, sub_path, pets_path, out_path = sys.argv[1:5]
    b = open(route_path, "rb").read()
    zone_key = zones(sub_path)
    souls = json.load(open(pets_path, encoding="utf-8"))["souls"]
    craft = bytes(c ^ XOR[k % 8] for k, c in enumerate("CraftGet".encode("utf-16-le")))
    out, bad = {}, []
    for item_id, pet in souls.items():
        at = b.find(struct.pack("<I", int(item_id)))
        if at < 0:
            continue
        end = b.find(craft, at)
        seg = b[at:end if end > 0 else at + 20000]
        found = set()
        for i in range(len(seg) - 20):
            g, npc, m, z, o = struct.unpack_from("<5I", seg, i)
            if (g == 0 or 1_000_000 <= g <= 1_999_999) and 2_000_000 <= npc <= 2_999_999 and m in GROUPS and 1 <= z <= 99999 and 1 <= o <= 9:
                found.add((m, z, npc))
        expected = struct.unpack_from("<i", seg, 4)[0]  # the element count stands right behind the item id
        if len(found) != expected:  # it must match what was found
            bad.append((item_id, pet, expected, len(found)))
        if found:
            out.setdefault(str(pet), []).extend([m, z, zone_key.get(z), npc] for m, z, npc in sorted(found))
    json.dump(out, open(out_path, "w"), separators=(",", ":"))
    print(len(out), "pets with client places;", sum(len(v) for v in out.values()), "entries;", len(bad), "rows with fewer entries than listed:", bad[:10])


main()
