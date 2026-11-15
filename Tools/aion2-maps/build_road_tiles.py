"""Builds the road layer of the world maps: python3 build_road_tiles.py <dump dir> <Client/assets/aion2/maps>

<dump dir> holds the road textures dumped with Tools/aion2-icons Extract (`dump <dir> <Map>_TransparentMapPath`). For every tile that has
any road the script writes `<key>/<col>_<row>_p.png` (RGBA, the roads in yellow as the game lays them over the ground tiles); empty tiles
are left out. The ground tiles stay as they are, so the client can show the map with the roads or without them."""
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import path_tiles  # noqa: E402

# map key -> (road texture family, tile size of the ground tiles: the client ships Altgard and the Abyss at 1024 px, so the roads get that size too)
MAPS = {"verteron": ("World_L_A", 2048), "altgard": ("World_D_A", 1024), "abyss": ("Abyss_Reshanta_A", 1024)}


def main():
    dump, assets = sys.argv[1:3]
    for key, (name, tile) in MAPS.items():
        target = os.path.join(assets, key)
        n = size = 0
        for old in [f for f in os.listdir(target) if f.endswith("_p.png")]:
            os.remove(os.path.join(target, old))
        for col in range(8):
            for row in range(8):
                src = os.path.join(dump, f"{name}_TransparentMapPath_{col:02d}_{row:02d}.uasset")
                if not os.path.exists(src):
                    continue
                im = path_tiles.load(src)
                if im is None:
                    continue
                # faint alpha (below 24 of 255) is compression noise, not road: cut it away so empty tiles are empty and the rest compresses well
                alpha = im.getchannel("A").point(lambda v: 0 if v < 24 else v)
                if alpha.getbbox() is None:
                    continue
                clear = alpha.point(lambda v: 255 if v else 0)
                rgb = Image.composite(im.convert("RGB"), Image.new("RGB", im.size, (0, 0, 0)), clear)
                im = rgb.convert("RGBA")
                im.putalpha(alpha)
                if im.width != tile:
                    im = im.resize((tile, tile), Image.LANCZOS)
                out = os.path.join(target, f"{col}_{row}_p.png")
                im.save(out, "PNG", optimize=True)
                n += 1
                size += os.path.getsize(out)
        print(f"{key}: {n} road tiles, {size // 1024} KB")


if __name__ == "__main__":
    main()
