"""Builds the map tiles of the instances (dungeons) for the client's pet map and writes assets/aion2/maps/instances.json.

Usage: python3 build_instance_maps.py <png dir> <umap json dir> <Dungeon.bin> <zones.json> <spawns.json> <out dir (assets/aion2/maps)>

Inputs (all dumped from the installed client with Tools/aion2-icons Extract, see README):
  <png dir>/<Map>/<Map>_<col>_<row>.png and <Map>_Masked_<col>_<row>.png   (export <out> UI/Map/WorldMap/<folder>/Res/)
  <umap json dir>/(WorldMap|SharedMap)_<Map>.json                            (json <out> .../WorldMap_<Map>.umap, needs the usmap)
  Dungeon.bin                                                                (dectable.py of Data/Table/Dungeon.dat)

What is in the data:
- Every instance has a map folder (UI/Map/WorldMap/<Map> or .../SharedMap/<Map>) with square 2048 px textures per sector (SectorIndex X = column,
  Y = row) and, for most, a "Masked" texture that is white where the floor is. The UMap's WorldMapBounds box says how much of the UI plane the
  map covers: its relative location is the centre c and its scale the half width h (UI units; Verteron is 8192).
- The zone frame's id of an instance is the id of its Dungeon.dat row; the row carries the map's text key STR_Map_<Map>.
- World -> map (fitted 2026-10-18 on 4,000+ monster spawn points of ~25 maps, 98 % of them on the floor mask): the UI scale is 0.0196 UI units
  per world unit like on the open maps (0.01958 .. 0.01976) and the map's centre sits at UI (2c): pixel fraction = 0.5 + (world * 0.0196 - 2c) / (2h),
  x to the right and y down, no flip or rotation (checked with fits of scale and offset per map: the offset came out as -c within a few UI units).
- Only instances with pet monsters get a map (the pet map has nothing else to show there; there are no collectibles in instances' data).
- Not built: maps whose plane is not square in UI units (Dungeon_L_0004, Dungeon_D_0011, RootCellar: wider than high or the other way round) and
  the sector grids that are not square (Dungeon_0005 1x5, ReforgedAbyss 4x5): the client's map model has one scale and a square grid.
"""
import glob
import json
import os
import struct
import sys

from PIL import Image

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "aion2-icons"))
import dat  # noqa: E402

UI_PER_WORLD = 0.0196
BG = (30, 40, 44)
# RootCellar: its spawn points do not lie on the floor of its map at all (1 of 10), so the mapping is not known
EXCLUDE = {"RootCellar"}


def main():
    png, jsons, dungeon_bin, zones_path, spawns_path, out = sys.argv[1:7]
    zones = {z["id"]: z for z in json.load(open(zones_path))["zones"] if z["kind"] == "instance"}
    spawn_tables = list(json.load(open(spawns_path))["maps"].keys())

    # zone id -> map folder name, from the Dungeon.dat rows
    b = open(dungeon_bin, "rb").read()
    ss = dat.strings(b)
    starts = [(struct.unpack_from("<i", b, o - 4)[0], k) for k, (o, s, e) in enumerate(ss)]
    starts = [(i, k) for i, k in starts if i in zones]
    zone_map = {}
    for n, (zid, k) in enumerate(starts):
        end = starts[n + 1][1] if n + 1 < len(starts) else len(ss)
        row = [s for _, s, _ in ss[k:end]]
        name = next((s for s in row if s.startswith("STR_Map_")), row[0]).replace("STR_Map_", "")
        zone_map.setdefault(name, set()).add(zid)

    maps = {}
    skipped = []
    for name, idset in sorted(zone_map.items()):
        ids = sorted(idset)
        files = glob.glob(os.path.join(jsons, f"*_{name}.json"))
        folder = os.path.join(png, name)
        if name in EXCLUDE or not files or not os.path.isdir(folder):
            continue
        ex = json.load(open(files[0]))
        box = next(e for e in ex if e["Type"] == "BoxComponent")["Properties"]
        loc, sc = box.get("RelativeLocation", {}), box["RelativeScale3D"]
        cx, cy, hx, hy = loc.get("X", 0.0), loc.get("Y", 0.0), sc["X"], sc["Y"]
        sectors = [e["Properties"].get("SectorIndex", {"X": 0, "Y": 0}) for e in ex if e["Type"] == "BP_WorldMapPlane_C"]
        gx = max(s.get("X", 0) for s in sectors) + 1
        gy = max(s.get("Y", 0) for s in sectors) + 1
        if gx != gy or abs(hx - hy) > 0.02 * hx:
            skipped.append(f"{name} (grid {gx}x{gy}, half {hx:.0f}x{hy:.0f})")
            continue
        tables = [t for t in spawn_tables if os.path.basename(t) == name or os.path.basename(t).startswith(name + "_")]
        if not tables:
            skipped.append(f"{name} (no pet monsters: nothing to show on it)")
            continue
        grid, tile = gx, 2048
        whole = grid * tile
        target = os.path.join(out, name)
        os.makedirs(target, exist_ok=True)
        masked = os.path.exists(os.path.join(folder, f"{name}_Masked_00_00.png"))
        for col in range(grid):
            for row in range(grid):
                base = Image.open(os.path.join(folder, f"{name}_{col:02d}_{row:02d}.png")).convert("RGB")
                if masked:
                    alpha = Image.open(os.path.join(folder, f"{name}_Masked_{col:02d}_{row:02d}.png")).convert("L")
                else:
                    alpha = Image.new("L", base.size, 255)
                rgb = Image.new("RGB", base.size, BG)
                rgb.paste(base, mask=alpha)
                rgb.save(os.path.join(target, f"{col}_{row}.jpg"), "JPEG", quality=80, optimize=True)
                alpha.resize((tile // 2, tile // 2), Image.BILINEAR).save(os.path.join(target, f"{col}_{row}_a.png"), optimize=True)
        scale = UI_PER_WORLD * whole / (2 * hx)
        maps[name] = {
            "title": (zones[ids[0]]["names"].get("en") or name),
            "table": tables[0] if tables else "",
            "tables": tables,
            "tile": tile,
            "grid": grid,
            "scale": scale,
            "offsetX": whole * (0.5 - cx / hx),
            "offsetY": whole * (0.5 - cy / hy),
            "zones": sorted(ids),
            "masked": masked,
        }
        print(name, grid, "x", grid, "zones", sorted(ids)[:4], "masked" if masked else "opaque")
    json.dump({"maps": maps}, open(os.path.join(out, "instances.json"), "w"), indent=1)
    print(len(maps), "instance maps; skipped:", "; ".join(skipped))


if __name__ == "__main__":
    main()
