"""Converts the dumped Pantheon decoration textures (Item/ETC/icon_AD_*, 256 px DXT5; see README.md, `dump <outdir> icon_AD_`) into 160 px WebP files.

Usage:  python3 -I convert_pantheon_icons.py <dir with the dumped .uasset files> <out dir> <pantheon.json>
Only the icons the Pantheon table names are written (Web-Frontend/images/aion2/pantheon).
"""
import io
import json
import os
import struct
import sys

from PIL import Image

raw, out, table = sys.argv[1:4]
os.makedirs(out, exist_ok=True)


def dds(w, h, data):
    header = struct.pack('<4sIIIIIII44sII4sIIIII', b'DDS ', 124, 0x81007, h, w, len(data), 0, 1, b'\0' * 44, 32, 4, b'DXT5', 0, 0, 0, 0, 0)
    return header + struct.pack('<IIIII', 0x1000, 0, 0, 0, 0) + data


names = sorted({row['i'] for row in json.load(open(table, encoding='utf-8'))['decorations'].values() if row.get('i')})
done = 0
for name in names:
    path = os.path.join(raw, name + '.uasset')
    if not os.path.exists(path):
        continue
    data = open(path, 'rb').read()
    i = data.rfind(b'PF_')
    fmt = data[i:data.find(b'\0', i)].decode()
    w, h, _ = struct.unpack('<iii', data[i - 16:i - 4])
    if fmt != 'PF_DXT5':
        continue
    n = w * h  # the pixels sit 12 bytes before the end of the package
    image = Image.open(io.BytesIO(dds(w, h, data[len(data) - n - 12:len(data) - 12]))).convert('RGBA').resize((160, 160), Image.LANCZOS)
    image.save(os.path.join(out, name + '.webp'), 'WEBP', quality=82, method=6)
    done += 1
print('written', done, 'of', len(names))
