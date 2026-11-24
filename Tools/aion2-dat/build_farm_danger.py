"""Writes the places of the dangers on every map that has pet monsters: the guards ("Wachmann", the NPC ids of Aion2Guards.IsGuard) and the elite / hero / legend
monsters that are not pet monsters. The farm map keeps its spots away from them.

Usage:  python3 build_farm_danger.py <dir with the unpacked AION2/Content/Data/Map> <NpcData.json (decoded)> <pets/spawns.json> <out.json>

Output: {"maps": {map table: {"g": [[x, y], ...], "e": [[x, y], ...]}}} in the game's world units (the same as spawns.json). The map tables are the ones of spawns.json,
so a map that has pets has its dangers; the place of one danger is listed once per spawn point.
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_spawns as spawns  # noqa: E402


def is_guard(n):
    return (2930813 <= n <= 2930820 or 2931239 <= n <= 2931242 or 2989445 <= n <= 2989448
            or n in (2939024, 2950012, 2950139, 2950619, 2950727, 2950739, 2950744, 2950839, 2950973, 2951099, 2951100, 2959287, 2959288))


def main():
    root, npc_path, spawns_path, out_path = sys.argv[1:5]
    npc = {x["ID"]["Value"]: x for x in json.load(open(npc_path))["Properties"]["Data"]}
    pet_maps = json.load(open(spawns_path))["maps"]
    pet_npcs = {int(i) for table in pet_maps.values() for i in table}
    maps = {}
    guards = elites = 0
    for folder, _, files in os.walk(root):
        if "MapData.dat" not in files:
            continue
        name = os.path.relpath(folder, root).replace(os.sep, "/")
        if name not in pet_maps:
            continue
        g, e = [], []
        for _, ids, pos in spawns.parse(os.path.join(folder, "MapData.dat")):
            if not all(abs(c) < 1e7 for c in pos):
                continue
            for i in ids:
                if is_guard(i):
                    g.append([round(pos[0]), round(pos[1])])
                elif i not in pet_npcs and i in npc and npc[i]["NpcSubType"].split("::")[-1] in ("EliteMonster", "HeroMonster", "LegendMonster"):
                    e.append([round(pos[0]), round(pos[1])])
        if g or e:
            maps[name] = {"g": g, "e": e}
            guards += len(g)
            elites += len(e)
    json.dump({"maps": maps}, open(out_path, "w"), separators=(",", ":"))
    print(f"{len(maps)} maps, {guards} guards, {elites} elite/hero spawns -> {out_path} ({os.path.getsize(out_path) // 1024} KB)")


if __name__ == "__main__":
    main()
