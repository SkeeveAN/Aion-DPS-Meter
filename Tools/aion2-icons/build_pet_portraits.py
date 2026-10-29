"""Pet pictures for the website and the partner API: Web-Frontend/images/aion2/pets/<pet key>.webp (256 px).

The game keeps one portrait per pet (UI/Resource/Texture/Portrait/Portrait_Vehicle/UT_Vehicle_Portrait_<key>.uasset, DXT5 256x256, the
colour variants end in _CV<n> and are left out). Dump the packages first (see README), then:
  python3 Tools/aion2-icons/build_pet_portraits.py <dir with the dumped UT_Vehicle_Portrait_*.uasset>
"""
import io
import json
import os
import struct
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Web-Frontend", "images", "aion2", "pets")
RAW = sys.argv[1]


def dds(w, h, data):
    header = struct.pack("<4sIIIIIII44sII4sIIIII", b"DDS ", 124, 0x81007, h, w, len(data), 0, 1, b"\0" * 44, 32, 4, b"DXT5", 0, 0, 0, 0, 0)
    return header + struct.pack("<IIIII", 0x1000, 0, 0, 0, 0) + data


os.makedirs(OUT, exist_ok=True)
keys = {p["key"] for p in json.load(open(os.path.join(ROOT, "Client", "assets", "aion2", "pets", "pets.json"), encoding="utf-8"))["pets"].values()}
done = 0
for key in sorted(keys):
    path = os.path.join(RAW, f"UT_Vehicle_Portrait_{key}.uasset")
    if not os.path.exists(path):
        print("missing", key)
        continue
    d = open(path, "rb").read()
    i = d.rfind(b"PF_")
    if d[i:d.find(b"\0", i)] != b"PF_DXT5":
        print("format", key)
        continue
    w, h, _ = struct.unpack("<iii", d[i - 16:i - 4])
    n = w * h
    Image.open(io.BytesIO(dds(w, h, d[len(d) - n - 12:len(d) - 12]))).convert("RGBA").save(os.path.join(OUT, key + ".webp"), "WEBP", quality=85, method=6)
    done += 1
print("written", done)
