import json, base64, struct, sys
def lz4(src, size):
    out = bytearray(); i = 0
    while i < len(src):
        t = src[i]; i += 1; lit = t >> 4
        if lit == 15:
            while True:
                b = src[i]; i += 1; lit += b
                if b != 255: break
        out += src[i:i+lit]; i += lit
        if i >= len(src): break
        off = src[i] | src[i+1] << 8; i += 2
        ml = t & 15
        if ml == 15:
            while True:
                b = src[i]; i += 1; ml += b
                if b != 255: break
        ml += 4
        if off == 0 or off > len(out): return None
        for _ in range(ml): out.append(out[-off])
    return bytes(out) if len(out) == size else None
def varint(b, p):
    v = 0; s = 0; n = 0
    while p + n < len(b):
        c = b[p+n]; n += 1; v |= (c & 0x7f) << s; s += 7
        if not c & 0x80: return v, n
    return None, 0
def split(buf):
    """yield (body) frames; returns remainder"""
    p = 0
    while p < len(buf):
        L, n = varint(buf, p)
        if L is None: break
        total = L + n - 4
        if total < n + 2 or p + total > len(buf):
            if total < n + 2: p += 1; continue
            break
        yield buf[p+n:p+total]
        p += total
    split.rest = buf[p:]
def iter_frames(path, from_server=True):
    """Yield (timestamp, body) with bundles expanded; stream reassembled by Seq."""
    buf = b''; nxt = None; last_conn = None
    with open(path, encoding='utf-8') as f:
        for line in f:
            try: r = json.loads(line)
            except ValueError: continue
            if r['Server'] != from_server: continue
            d = base64.b64decode(r['Data']); seq = r['Seq']
            conn = (r.get('From'), r.get('To'))
            if conn != last_conn:
                last_conn = conn; buf = b''; nxt = None   # another connection (zone or server change)
            if nxt is not None and seq != nxt:
                diff = (seq - nxt) & 0xffffffff
                if diff > 0x80000000:
                    if (nxt - seq) & 0xffffffff < 0x1000000: continue   # retransmit
                    buf = b''   # a new connection (relog, server change): the sequence numbers start over
                else:
                    buf = b''   # gap: resync
            nxt = (seq + len(d)) & 0xffffffff
            buf += d
            frames = list(split(buf)); buf = split.rest
            for fr in frames:
                if fr[:2] == b'\xff\xff' and len(fr) > 6:
                    size = struct.unpack('<I', fr[2:6])[0]
                    raw = lz4(fr[6:], size)
                    if raw:
                        for sub in split(raw): yield r['T'], sub
                    continue
                yield r['T'], fr
