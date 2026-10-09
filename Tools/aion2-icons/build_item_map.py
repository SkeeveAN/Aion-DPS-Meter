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
def alias(name):
    """The client has two names for the same inventory icon: Icon_WP_DA_0078_T05 / Icon_Equip_WP_L_DA_0078_T05,
    Icon_GM_0028_T05_Shoulder / Icon_Equip_AR_L_0028_T05_Shoulder, Icon_Equip_ACC_Rune_R_002 / Icon_Acc_Rune_R_002.
    Same number, tier and slot only; a different tier is a different icon and is not substituted."""
    n = name.lower()
    candidates = []
    if m := re.fullmatch(r'icon_wp_([a-z]+)_(\d+)_(t\d+)', n):
        candidates = [f'icon_equip_wp_{side}_{m[1]}_{m[2]}_{m[3]}' for side in 'lr']
    elif m := re.fullmatch(r'icon_gm_(\d+)_(t\d+)_(\w+)', n):
        candidates = [f'icon_equip_ar_{side}_{m[1]}_{m[2]}_{m[3]}' for side in 'lr']
    elif m := re.fullmatch(r'icon_gm_([a-z]+_\d+)_(\w+)', n):  # named sets, e.g. Icon_GM_Idris_01_Torso
        candidates = [f'icon_equip_ar_{side}_{m[1]}_{m[2]}' for side in 'lr']
    candidates.append(n.replace('equip_acc_', 'acc_'))
    return next((tex[c] for c in candidates if c in tex), None)


res = {}
for k in json.load(open(info_path)):
    ic = next((s for s in rows.get(int(k), []) if re.match(r'(?i)icon_', s)), None)
    if ic and ic.lower() in tex:
        res[k] = tex[ic.lower()]
    elif ic and (a := alias(ic)):
        res[k] = a
json.dump(res, open(out, 'w'), separators=(',', ':'))
print(len(res), 'items with an icon')
