import sys, os; sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from frames import iter_frames
op = bytes.fromhex(sys.argv[2]); lim = int(sys.argv[3]); n = 0
for t, fr in iter_frames(sys.argv[1]):
    if fr[:2] == op:
        print(t[11:23], len(fr), fr[2:].hex()); n += 1
        if n >= lim: break
