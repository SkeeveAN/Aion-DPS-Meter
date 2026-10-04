"""Daevanion nodes from the client's DaevanionNode table: id -> [board, row, col, grade, type, token, value].
Usage: build_nodes.py <tables dir> <old daevanion_nodes.json (for the board names)> <out.json>"""
import json, struct, sys
import dat
tables, old, out = sys.argv[1:4]
d = open(f'{tables}/DaevanionNode.bin', 'rb').read()
ss = dat.strings(d)
starts = {}
for i, (o, t, e) in enumerate(ss):
    if t.startswith('EItemGrade::') and ss[i + 1][1].startswith('EDaevanionNodeType::'):
        starts[struct.unpack_from('<i', d, o - 16)[0]] = (o - 16, t[12:], ss[i + 1][1][20:])
ids = sorted(starts)
nodes = {}
for n, nid in enumerate(ids):
    a, grade, kind = starts[nid]
    if kind == 'None':
        continue
    b = starts[ids[n + 1]][0] if n + 1 < len(ids) else len(d)
    q = a + 16
    for _ in range(4):
        q += 4 + (-struct.unpack_from('<i', d, q)[0]) * 2
    plain, p = [], q
    while p < b - 4:  # plain (not obfuscated) ASCII strings: stat token + value, or skill id + level
        v = struct.unpack_from('<i', d, p)[0]
        if 2 <= v <= 80 and p + 4 + v <= b and d[p + 4 + v - 1] == 0 and all(32 <= c < 127 for c in d[p + 4:p + 3 + v]):
            plain.append(d[p + 4:p + 3 + v].decode()); p += 4 + v
        else:
            p += 1
    board, row, col = struct.unpack_from('<3i', d, a + 4)
    token = plain[0] if plain else ''
    value = 0
    if len(plain) > 1:
        f = float(plain[1]); value = int(f) if f == int(f) else f
    nodes[str(nid)] = [board, row, col, grade, kind, token, value]
boards = json.load(open(old))['boards']
json.dump({'boards': boards, 'nodes': nodes}, open(out, 'w'), separators=(',', ':'))
print(len(nodes), 'nodes')
