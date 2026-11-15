"""Reads the road layer of a world map tile ("<Map>_TransparentMapPath_<col>_<row>", dumped with Tools/aion2-icons Extract) as an RGBA picture.

The texture is a 2048 px DXT5 picture with the roads in the alpha channel (yellow lines); the game lays it over the map's ground tiles."""
import io
import struct

from PIL import Image


def dds(w, h, fourcc, data):
    hdr = struct.pack('<4sIIIIIII44sII4sIIIII', b'DDS ', 124, 0x81007, h, w, len(data), 0, 1, b'\0' * 44, 32, 4, fourcc, 0, 0, 0, 0, 0)
    return hdr + struct.pack('<IIIII', 0x1000, 0, 0, 0, 0) + data


def load(path):
    d = open(path, 'rb').read()
    i = d.rfind(b'PF_')
    j = d.find(b'\0', i)
    fmt = d[i:j].decode()
    w, h, _ = struct.unpack('<iii', d[i - 16:i - 4])
    if fmt != 'PF_DXT5':
        return None
    n = w * h
    return Image.open(io.BytesIO(dds(w, h, b'DXT5', d[len(d) - n - 12:len(d) - 12]))).convert('RGBA')
