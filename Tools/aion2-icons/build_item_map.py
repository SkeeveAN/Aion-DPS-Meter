"""Item id -> icon texture name, from the decrypted Item.dat (see README). Usage: build_item_map.py <tables dir> <files.txt> <item_info.json> <out.json>"""
import json, re, struct, sys
import dat
tables, files, info_path, out = sys.argv[1:5]
tex = {l.strip().rsplit('/', 1)[-1][:-7].lower(): l.strip().rsplit('/', 1)[-1][:-7] for l in open(files) if '/Resource/Texture/' in l and l.strip().endswith('.uasset')}
d = open(f'{tables}/Item.bin', 'rb').read()
ss = dat.strings(d)
rows, cur = {}, None
for i, (o, t, e) in enumerate(ss):
    # a row is [int id][name][STR_ITEM_...]; the icon is the first Icon_* string after it
    if t.startswith('STR_ITEM_') and i > 0 and not ss[i - 1][1].startswith('STR_') and ss[i - 1][2] + 4 >= o - 8:
        cur = rows.setdefault(struct.unpack_from('<i', d, ss[i - 1][0] - 4)[0], [])
    elif cur is not None and len(cur) < 60:
        cur.append(t)
res = {}
for k in json.load(open(info_path)):
    ic = next((s for s in rows.get(int(k), []) if re.match(r'(?i)icon_', s)), None)
    if ic and ic.lower() in tex:
        res[k] = tex[ic.lower()]
json.dump(res, open(out, 'w'), separators=(',', ':'))
print(len(res), 'items with an icon')
