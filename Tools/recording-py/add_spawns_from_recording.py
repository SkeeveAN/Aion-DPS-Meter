"""Adds spawn points seen in a recording (frame 0x4136) to Client/assets/aion2/pets/spawns.json for the Horn Den.

The map file of the dungeon holds no spawn point for some of its pet monsters (the Duduka are set by script). The recording shows where they
appear. The three difficulties share their places, so a point seen on one is written to all three tables (230xxxx normal, 231xxxx easy, 232xxxx hard).

Usage: python3 -I add_spawns_from_recording.py <recording.jsonl>... [--dry]
"""
import json, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from frames import iter_frames

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Client", "assets", "aion2", "pets")
DIFF = {2300000: "Normal", 2310000: "Easy", 2320000: "Hard"}
NEAR = 800  # world units (8 m)

args = [a for a in sys.argv[1:] if not a.startswith("--")]
dry = "--dry" in sys.argv
monsters = {int(k) for k in json.load(open(os.path.join(ROOT, "pets.json")))["monsters"]}
spath = os.path.join(ROOT, "spawns.json")
data = json.load(open(spath))
maps = data["maps"]
def folder_of(table):
    return table.rsplit("_", 1)[0] if table.rsplit("_", 1)[-1] in DIFF.values() else None


# the party dungeon a monster id belongs to: the folder whose tables hold ids of the same family (prefix 230/231/232, suffix within 30 of it)
def dungeon_of(npc):
    base, suffix = npc // 10000 * 10000, npc % 10000
    if base not in DIFF or "Dungeon/Party/" not in "".join(maps):
        return None
    for table, ids in maps.items():
        folder = folder_of(table)
        if folder and folder.startswith("Dungeon/Party/") and table.endswith("_" + DIFF[base]):
            if any(int(i) // 10000 * 10000 == base and abs(int(i) % 10000 - suffix) < 30 for i in ids):
                return folder
    return None


# only the monsters the map files lack (set by script) are taken from recordings; those already taken are listed in the sidecar file and keep growing
SIDECAR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "recorded_spawn_ids.json")
taken = set(json.load(open(SIDECAR))) if os.path.exists(SIDECAR) else set()


def lacks_points(folder, suffix):
    return all(not maps.get(f"{folder}_{d}", {}).get(str(b + suffix)) for b, d in DIFF.items())


seen = {}
for path in args:
    for _, fr in iter_frames(path):
        if fr[:2] != b"\x41\x36":
            continue
        for i in range(2, len(fr) - 20):
            n = struct.unpack_from("<I", fr, i)[0]
            if n in monsters and n // 10000 * 10000 in DIFF:
                x, y, z = struct.unpack_from("<fff", fr, i + 6)
                if abs(x) < 1e6 and abs(y) < 1e6 and abs(z) < 1e6 and (folder := dungeon_of(n)) and (f"{folder}.{n % 10000}" in taken or lacks_points(folder, n % 10000)):
                    seen.setdefault((folder, n % 10000), []).append((round(x), round(y), round(z)))
                break

added = 0
for (folder, suffix), points in sorted(seen.items()):
    for base, diff in DIFF.items():
        npc = str(base + suffix)
        if int(npc) not in monsters:
            continue
        have = maps.setdefault(f"{folder}_{diff}", {}).setdefault(npc, [])
        for p in points:
            if not any(abs(p[0] - q[0]) < NEAR and abs(p[1] - q[1]) < NEAR and abs(p[2] - q[2]) < 1500 for q in have):
                have.append(list(p))
                added += 1
print("added", added, "points;", {f"{f.rsplit('/', 1)[-1]}.{s}": len(p) for (f, s), p in seen.items()}, "seen")
if not dry:
    json.dump(sorted(taken | {f"{f}.{sfx}" for f, sfx in seen}), open(SIDECAR, "w"))
    json.dump(data, open(spath, "w"), separators=(",", ":"))
