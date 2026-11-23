"""Lists the pet monsters a recording shows (spawn frame 0x4136) that have no spawn point in spawns.json, with where they were seen.

Usage: python3 -I missing_pet_spawns.py <recording.jsonl>...
"""
import collections, json, os, struct, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from frames import iter_frames

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Client", "assets", "aion2", "pets")
P = json.load(open(os.path.join(ROOT, "pets.json")))
monsters = {int(k): v for k, v in P["monsters"].items()}
have = {int(n) for m in json.load(open(os.path.join(ROOT, "spawns.json")))["maps"].values() for n in m}
seen = collections.defaultdict(list)
for path in sys.argv[1:]:
    for t, fr in iter_frames(path):
        if fr[:2] != b"\x41\x36":
            continue
        for i in range(2, len(fr) - 20):
            n = struct.unpack_from("<I", fr, i)[0]
            if n in monsters:
                x, y, z = struct.unpack_from("<fff", fr, i + 6)
                if abs(x) < 1e6 and abs(y) < 1e6 and abs(z) < 1e6:
                    seen[n].append((round(x), round(y), round(z)))
                break
for n, pts in sorted(seen.items()):
    if n not in have:
        print(n, P["pets"][str(monsters[n])]["names"]["de"], len(pts), "x, e.g.", pts[:3])
print("pet monsters seen:", len(seen), "- without spawn point:", sum(1 for n in seen if n not in have))
