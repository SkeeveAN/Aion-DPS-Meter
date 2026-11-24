"""Adds the places pet monsters of the open world were seen at (spawn frame 0x4136) to Client/assets/aion2/pets/spawns.json.

The map files hold only part of the places of some world monsters (the Coradon around the Fronarbeiter's Rest had two of nine in the list). A sighting is
added to the table the monster already has points in, when it lies inside that table's area (so a dungeon or an instance, which uses other coordinates,
never lands in the world) and is not within 8 m of a point the monster has there.

Usage: python3 -I add_world_spawns_from_recording.py <recording.jsonl>... [--dry]
"""
import json, math, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from frames import iter_frames

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Client", "assets", "aion2", "pets")
NEAR = 800      # world units (8 m)
MARGIN = 3000   # a sighting this far outside the area of all points of a table still counts
args = [a for a in sys.argv[1:] if not a.startswith("--")]
dry = "--dry" in sys.argv
monsters = {int(k) for k in json.load(open(os.path.join(ROOT, "pets.json")))["monsters"]}
spath = os.path.join(ROOT, "spawns.json")
data = json.load(open(spath))
maps = data["maps"]
world = [t for t in maps if t.startswith("World/")]
box = {}
for t in world:
    pts = [p for l in maps[t].values() for p in l]
    if pts:
        box[t] = (min(p[0] for p in pts) - MARGIN, max(p[0] for p in pts) + MARGIN, min(p[1] for p in pts) - MARGIN, max(p[1] for p in pts) + MARGIN)

seen = {}
for path in args:
    for _, fr in iter_frames(path):
        if fr[:2] != b"\x41\x36":
            continue
        for i in range(2, len(fr) - 20):
            n = struct.unpack_from("<I", fr, i)[0]
            if n in monsters and 2000000 < n < 2300000:
                x, y, z = struct.unpack_from("<fff", fr, i + 6)
                if abs(x) < 1e6 and abs(y) < 1e6 and abs(z) < 1e6 and x != 0:
                    seen.setdefault(n, []).append((round(x), round(y), round(z)))
                break

added = {}
for n, sightings in sorted(seen.items()):
    tables = [t for t in world if t in box and maps[t].get(str(n))]
    for p in sightings:
        for t in tables:
            x0, x1, y0, y1 = box[t]
            if not (x0 <= p[0] <= x1 and y0 <= p[1] <= y1):
                continue
            have = maps[t][str(n)]
            if not any(math.hypot(p[0] - q[0], p[1] - q[1]) < NEAR for q in have):
                have.append(list(p))
                added[n] = added.get(n, 0) + 1
            break
print("added", sum(added.values()), "points for", len(added), "monsters;", dict(sorted(added.items(), key=lambda kv: -kv[1])[:12]))
if not dry:
    json.dump(data, open(spath, "w"), separators=(",", ":"))
