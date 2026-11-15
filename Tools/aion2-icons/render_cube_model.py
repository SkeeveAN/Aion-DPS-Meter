"""Renders the game's own 3D model of the Hidden Cube (HiddenCube_01) to a picture: python3 render_cube_model.py <glb> <diffuse .uasset> <fx .uasset> <out.png> [size]

The glb comes from `Extract <Paks> mesh <out> EnvOBJ/Common/HiddenCube_01/HiddenCube_01.uasset` (the model, without its textures); the two textures are the
game's HiddenCube_01_D (the stone with its bone-coloured bands) and HiddenCube_01_FX (the glowing glyphs, read as an emissive layer). The cube is seen from a
corner (isometric), lit from above left, drawn with a z-buffer; the glyph layer is added as a glowing gold, and a thin dark outline is put around it."""
import json
import math
import struct
import sys

from PIL import Image, ImageFilter

sys.path.insert(0, __file__.rsplit("/", 1)[0])
import build_gather_icons as bg  # noqa: E402  (dds / convert helpers)


def load_texture(path):
    d = open(path, "rb").read()
    i = d.rfind(b"PF_")
    j = d.find(b"\0", i)
    fmt = d[i:j].decode()
    w, h, _ = struct.unpack("<iii", d[i - 16:i - 4])
    ub = path[:-7] + ".ubulk"
    bpp = 0.5 if fmt == "PF_DXT1" else 1.0
    data = open(ub, "rb").read()
    dim = w
    while dim > 4 and int(dim * dim * bpp) > len(data):
        dim //= 2
    data = data[:int(dim * dim * bpp)]
    import io
    return Image.open(io.BytesIO(bg.dds(dim, dim, b"DXT1" if fmt == "PF_DXT1" else b"DXT5", data))).convert("RGB")


def read_glb(path):
    b = open(path, "rb").read()
    off, chunks = 12, []
    while off < len(b):
        cl, ct = struct.unpack_from("<I4s", b, off)
        chunks.append(b[off + 8:off + 8 + cl])
        off += 8 + cl
    j, bin_ = json.loads(chunks[0]), chunks[1]
    prim = j["meshes"][0]["primitives"][0]

    def accessor(index):
        a = j["accessors"][index]
        view = j["bufferViews"][a["bufferView"]]
        n = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}[a["type"]]
        fmt = {5126: "f", 5123: "H", 5125: "I", 5121: "B"}[a["componentType"]]
        size = struct.calcsize(fmt)
        stride = view.get("byteStride", n * size)
        base = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        return [struct.unpack_from("<" + fmt * n, bin_, base + k * stride) for k in range(a["count"])]

    pos = accessor(prim["attributes"]["POSITION"])
    nor = accessor(prim["attributes"]["NORMAL"])
    uv = accessor(prim["attributes"]["TEXCOORD_0"])
    idx = [i[0] for i in accessor(prim["indices"])]
    return pos, nor, uv, idx


def main():
    glb, diffuse, fx, out = sys.argv[1:5]
    size = int(sys.argv[5]) if len(sys.argv) > 5 else 64
    pos, nor, uv, idx = read_glb(glb)
    tex, glow = load_texture(diffuse), load_texture(fx)
    tp, gp = tex.load(), glow.load()
    tw, gw = tex.width, glow.width
    S = 4 * size if size < 256 else size   # supersample
    inner = 0.80  # the cube fills this share of the picture, the rest is for the glow
    # isometric camera: yaw 45 degrees, pitch about 35 degrees; the model is Y up, centred at (0, 1.1, 0)
    yaw, pitch = math.radians(45), math.radians(35.26)
    cy, sy, cp, sp = math.cos(yaw), math.sin(yaw), math.cos(pitch), math.sin(pitch)

    def proj(p):
        x, y, z = p[0], p[1] - 1.1, p[2]
        x1, z1 = x * cy - z * sy, x * sy + z * cy
        y2, z2 = y * cp - z1 * sp, y * sp + z1 * cp
        return x1, y2, z2

    pts = [proj(p) for p in pos]
    nrm = [(n[0] * cy - n[2] * sy, n[1], n[0] * sy + n[2] * cy) for n in nor]
    nrm = [(n[0], n[1] * cp - n[2] * sp, n[1] * sp + n[2] * cp) for n in nrm]
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    scale = inner * S / max(max(xs) - min(xs), max(ys) - min(ys))
    ox, oy = S / 2 - (max(xs) + min(xs)) / 2 * scale, S / 2 + (max(ys) + min(ys)) / 2 * scale
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ip = img.load()
    emit = Image.new("L", (S, S), 0)
    ep = emit.load()
    zbuf = [[-1e9] * S for _ in range(S)]
    light = (-0.45, 0.8, 0.55)
    ln = math.sqrt(sum(c * c for c in light))
    light = tuple(c / ln for c in light)
    for t in range(0, len(idx), 3):
        a, b, c = idx[t], idx[t + 1], idx[t + 2]
        P = [(pts[k][0] * scale + ox, -pts[k][1] * scale + oy, pts[k][2]) for k in (a, b, c)]
        minx, maxx = max(0, int(min(p[0] for p in P))), min(S - 1, int(max(p[0] for p in P)) + 1)
        miny, maxy = max(0, int(min(p[1] for p in P))), min(S - 1, int(max(p[1] for p in P)) + 1)
        d = (P[1][1] - P[2][1]) * (P[0][0] - P[2][0]) + (P[2][0] - P[1][0]) * (P[0][1] - P[2][1])
        if abs(d) < 1e-9:
            continue
        for y in range(miny, maxy + 1):
            for x in range(minx, maxx + 1):
                w0 = ((P[1][1] - P[2][1]) * (x + 0.5 - P[2][0]) + (P[2][0] - P[1][0]) * (y + 0.5 - P[2][1])) / d
                w1 = ((P[2][1] - P[0][1]) * (x + 0.5 - P[2][0]) + (P[0][0] - P[2][0]) * (y + 0.5 - P[2][1])) / d
                w2 = 1 - w0 - w1
                if w0 < -1e-6 or w1 < -1e-6 or w2 < -1e-6:
                    continue
                z = w0 * P[0][2] + w1 * P[1][2] + w2 * P[2][2]
                if z <= zbuf[y][x]:
                    continue
                u = w0 * uv[a][0] + w1 * uv[b][0] + w2 * uv[c][0]
                v = w0 * uv[a][1] + w1 * uv[b][1] + w2 * uv[c][1]
                r, g, bl = tp[min(tw - 1, max(0, int(u * tw))), min(tw - 1, max(0, int(v * tw)))]
                n = [w0 * nrm[a][i] + w1 * nrm[b][i] + w2 * nrm[c][i] for i in range(3)]
                nl = math.sqrt(sum(q * q for q in n)) or 1
                lam = max(0.0, (n[0] * light[0] + n[1] * light[1] + n[2] * light[2]) / nl)
                shade = 0.35 + 0.9 * lam
                er, eg, eb = gp[min(gw - 1, max(0, int(u * gw))), min(gw - 1, max(0, int(v * gw)))]
                e = max(er, eg) / 255.0   # the glyphs are drawn in the red channel
                lum = (0.3 * r + 0.59 * g + 0.11 * bl) / 255.0
                hot = min(1.0, e * 2.0)
                # as in the game: the stone is lit red from inside (copper-red by its own brightness), the glyphs are almost white in the red
                R = min(255, int(255 * min(1.0, lum * shade * 1.35 + 0.05) + hot * 130))
                G = min(255, int(255 * min(1.0, lum * shade * 0.42) * 0.7 + hot * 185))
                B = min(255, int(255 * min(1.0, lum * shade * 0.34) * 0.6 + hot * 175))
                zbuf[y][x] = z
                ip[x, y] = (R, G, B, 255)
                ep[x, y] = int(255 * min(1.0, e * 1.6))
    # the cube glows: the glyphs light up and bleed into the stone around them (an additive orange bloom over the picture, where the cube is)
    soft = emit.filter(ImageFilter.GaussianBlur(max(2, S // size * 3)))
    wide = emit.filter(ImageFilter.GaussianBlur(max(4, S // size * 8)))
    cube_alpha = img.getchannel("A")
    px = img.load()
    sp_, wp_, ap_ = soft.load(), wide.load(), cube_alpha.load()
    for y in range(S):
        for x in range(S):
            if ap_[x, y]:
                k = min(1.0, sp_[x, y] / 255.0 * 1.3 + wp_[x, y] / 255.0 * 0.9)
                r, g, b, a = px[x, y]
                px[x, y] = (min(255, int(r + k * 200)), min(255, int(g + k * 40)), min(255, int(b + k * 35)), a)
    # dark outline of one pixel (at the final size) for contrast on the map, then the final size
    alpha = img.getchannel("A")
    ring = alpha.filter(ImageFilter.MaxFilter(2 * max(1, S // size) + 1))
    outline = Image.new("RGBA", (S, S), (30, 4, 4, 255))
    outline.putalpha(ring)
    # a soft red glow hugging the cube (not a frame): the stone alone is brown and grey like the map and gets lost on it
    halo = Image.new("RGBA", (S, S), (255, 40, 30, 255))
    halo.putalpha(alpha.filter(ImageFilter.MaxFilter(2 * max(1, S // size) * 2 + 1)).filter(ImageFilter.GaussianBlur(max(2, S // size * 2))).point(lambda v: min(255, int(v * 1.5))))
    final = Image.alpha_composite(Image.alpha_composite(halo, outline), img).resize((size, size), Image.LANCZOS)
    final.save(out, optimize=True)
    print("wrote", out, final.size)


if __name__ == "__main__":
    main()
