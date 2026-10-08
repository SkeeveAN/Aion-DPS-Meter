"""Exports the map tiles for the client: JPEG colour + a small PNG alpha mask per tile (WPF has no WebP decoder).

Usage: python3 export_client_maps.py <dir with Verteron_tiles, Altgard_tiles, Abyss_tiles> <out dir> <calibration.json>
Writes <out>/<map>/<col>_<row>.jpg and <col>_<row>_a.png (half size mask) and <out>/maps.json with everything the client needs:
the tile size, the grid, the world -> native pixel calibration and the key of the map's spawn table in pets/spawns.json.
"""
import json
import os
import sys

from PIL import Image

src, out, cal_path = sys.argv[1:4]
cal = json.load(open(cal_path))
MAPS = {
    "verteron": dict(folder="Verteron_tiles", table="World/World_L/World_L_A", title="Verteron", faction="Elyos"),
    "altgard": dict(folder="Altgard_tiles", table="World/World_D/World_D_A", title="Altgard", faction="Asmodian"),
    "abyss": dict(folder="Abyss_tiles", table="Intersever/Abyss/Abyss_Reshanta_A", title="Abyss", faction=""),
}
info = {"maps": {}}
total = 0
for key, m in MAPS.items():
    d = os.path.join(src, m["folder"])
    meta = json.load(open(os.path.join(d, "info.json")))
    tile, grid = meta["tile"], meta["grid"]
    os.makedirs(os.path.join(out, key), exist_ok=True)
    count = 0
    for f in sorted(os.listdir(d)):
        if not f.endswith(".webp"):
            continue
        name = f[:-5]
        im = Image.open(os.path.join(d, f)).convert("RGBA")
        rgb = Image.new("RGB", im.size, (30, 40, 44))
        rgb.paste(im, mask=im.getchannel("A"))
        rgb.save(os.path.join(out, key, name + ".jpg"), "JPEG", quality=82, optimize=True)
        im.getchannel("A").resize((tile // 2, tile // 2), Image.BILINEAR).save(os.path.join(out, key, name + "_a.png"), optimize=True)
        count += 1
    # calibration -> native pixels of the whole map (tile * grid)
    c = cal[m["title"]]
    native = tile * grid
    if key == "verteron":
        s, ox, oy = c["scale_px_per_unit_native"], c["offset_x_native"], c["offset_y_native"]
    else:
        k = native / 1024.0
        s, ox, oy = c["scale_px_per_unit_1024"] * k, c["offset_x_1024"] * k, c["offset_y_1024"] * k
    info["maps"][key] = {"title": m["title"], "table": m["table"], "tile": tile, "grid": grid, "scale": s, "offsetX": ox, "offsetY": oy, "tiles": count}
    size = sum(os.path.getsize(os.path.join(out, key, f)) for f in os.listdir(os.path.join(out, key)))
    total += size
    print(key, count, "tiles", size // 1024 // 1024, "MB")
json.dump(info, open(os.path.join(out, "maps.json"), "w"), indent=1)
print("total", total // 1024 // 1024, "MB")
