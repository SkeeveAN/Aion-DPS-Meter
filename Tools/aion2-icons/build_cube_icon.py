"""Draws the picture of the Hidden Cube for the gathering settings and the map: python3 build_cube_icon.py <Client/assets/aion2/maps/gather_icons>

The client has no icon of the cube (its item is a key), so this is drawn: a pale stone cube with bone-coloured edges (the game's HiddenCube_01_D texture is grey stone with bone-white bands) seen from a corner, a glowing orange ring
with six spokes on each of its three faces, and an orange glow around it - as the cube looks in the game. Output: HiddenCube.png (64 px, transparent)."""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

S = 512      # a face's own square
OUT = 64


def face(shade):
    """One flat face: bronze square with a frame, the ring, six spokes and the hub, glowing red."""
    base = tuple(int(c * shade) for c in (96, 88, 80))
    im = Image.new("RGBA", (S, S), base + (255,))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, S - 1, S - 1], outline=tuple(int(c * shade) for c in (206, 192, 168)), width=14)
    d.rectangle([22, 22, S - 23, S - 23], outline=tuple(int(c * shade) for c in (70, 60, 52)), width=6)
    cx = cy = S / 2
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    g.ellipse([cx - 200, cy - 200, cx + 200, cy + 200], fill=(50, 18, 4, 255))
    g.ellipse([cx - 200, cy - 200, cx + 200, cy + 200], outline=(255, 140, 20, 255), width=44)
    g.ellipse([cx - 140, cy - 140, cx + 140, cy + 140], outline=(255, 220, 130, 255), width=14)
    for k in range(6):
        a = math.radians(30 + k * 60)
        g.line([cx + math.cos(a) * 40, cy + math.sin(a) * 40, cx + math.cos(a) * 160, cy + math.sin(a) * 160], fill=(255, 175, 50, 255), width=24)
    g.ellipse([cx - 44, cy - 44, cx + 44, cy + 44], fill=(255, 150, 30, 255), outline=(255, 230, 170, 255), width=8)
    g.ellipse([cx - 16, cy - 16, cx + 16, cy + 16], fill=(90, 30, 6, 255))
    halo = glow.filter(ImageFilter.GaussianBlur(14))
    im = Image.alpha_composite(im, halo)
    return Image.alpha_composite(im, glow)


def main():
    out_dir = sys.argv[1]
    W = 1024
    c = (W / 2, W / 2)
    r = 400
    # the corner of the cube points at the viewer: the top, the left and the right face as parallelograms
    top_pt = (c[0], c[1] - r)
    ul = (c[0] - r * 0.866, c[1] - r / 2)
    ur = (c[0] + r * 0.866, c[1] - r / 2)
    mid = c
    ll = (c[0] - r * 0.866, c[1] + r / 2)
    lr = (c[0] + r * 0.866, c[1] + r / 2)
    bot = (c[0], c[1] + r)
    canvas = Image.new("RGBA", (W, W), (0, 0, 0, 0))

    def put(img, origin, u, v):
        # maps the square's corners (0,0) -> origin, (S,0) -> origin+u, (0,S) -> origin+v
        a, b = (u[0] - origin[0]) / S, (v[0] - origin[0]) / S
        d, e = (u[1] - origin[1]) / S, (v[1] - origin[1]) / S
        # PIL wants the inverse: output (x, y) -> input (X, Y)
        det = a * e - b * d
        inv = (e / det, -b / det, (b * origin[1] - e * origin[0]) / det, -d / det, a / det, (d * origin[0] - a * origin[1]) / det)
        tex = img.transform((W, W), Image.AFFINE, inv, Image.BICUBIC)
        canvas.alpha_composite(tex)

    put(face(1.0), ul, top_pt, mid)                # top face: its square's corners ul, top, mid
    put(face(0.62), ul, mid, ll)                   # left face
    put(face(0.80), mid, ur, bot)                  # right face
    # the outline of the cube and the glow
    silhouette = canvas.getchannel("A")
    edge = ImageDraw.Draw(canvas)
    for p, q in ((top_pt, ur), (ur, lr), (lr, bot), (bot, ll), (ll, ul), (ul, top_pt), (mid, ul), (mid, ur), (mid, bot)):
        edge.line([p, q], fill=(214, 198, 172, 255), width=10)
    glow = Image.new("RGBA", (W, W), (255, 120, 20, 255))
    glow.putalpha(silhouette.filter(ImageFilter.MaxFilter(21)).filter(ImageFilter.GaussianBlur(34)).point(lambda v: int(v * 0.85)))
    final = Image.alpha_composite(glow, canvas)
    bbox = final.getchannel("A").point(lambda v: 255 if v > 24 else 0).getbbox()
    final = final.crop(bbox)
    side = max(final.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(final, ((side - final.width) // 2, (side - final.height) // 2))
    square.resize((OUT, OUT), Image.LANCZOS).save(os.path.join(out_dir, "HiddenCube.png"), optimize=True)


if __name__ == "__main__":
    main()
