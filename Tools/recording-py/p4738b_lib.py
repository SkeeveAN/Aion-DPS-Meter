import struct, datetime
from frames import varint
def parse(b):
    c = b[0]; p = 1; ent = []
    for _ in range(c):
        if p + 4 > len(b): return None
        i = struct.unpack('<I', b[p:p+4])[0]; p += 4
        v, n = varint(b, p)
        if v is None: return None
        p += n; ent.append((i, v))
    return ent if p == len(b) else None
def ts(s): return datetime.datetime.fromisoformat(s).timestamp()
