import sys, os, glob; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from frames import iter_frames, varint
from p4738b_lib import ts
import struct, collections
res = collections.defaultdict(lambda: [0, 0])   # kind -> [with paired damage, without]
for D in sorted(glob.glob(sys.argv[1] + '/aion2-2026-10-1[123]*.jsonl')):
    if os.path.getsize(D) > 60_000_000: continue
    dm = collections.defaultdict(list); cand = []
    for t, fr in iter_frames(D):
        if fr[:2] != b'\x04\x38': continue
        b = fr[2:]; p = 0
        tg, n = varint(b, p); p += n
        fl, n = varint(b, p); p += n
        if tg is None or fl is None or len(b) < p + 12: continue
        p += 1
        ac, n = varint(b, p); p += n
        if ac is None or len(b) < p + 6: continue
        sk = struct.unpack('<I', b[p:p+4])[0]; p += 4
        tt = ts(t)
        if fl & 4: dm[(tg, ac, sk)].append(tt)
        elif ac != tg: cand.append(((tg, ac, sk), tt, b[p+1], 'PC' if sk >= 10_000_000 else 'NPC'))
    for key, tt, kind, who in cand:
        paired = any(abs(x - tt) < 0.5 for x in dm.get(key, ()))
        res[(who, kind)][0 if paired else 1] += 1
for k, (a, b) in sorted(res.items()): print(k, 'mit Schaden-Partner', a, 'ohne', b)
