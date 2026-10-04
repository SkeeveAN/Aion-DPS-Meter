"""Skill id -> icon texture name from the client's Skill.dat: the row's icon path ('Skill/ICON_xx_SKILL_nnn', plain text)
follows the row's STR_SKILL_PC_<CLASS>_<id> string, before the next row's string. Usage: build_skill_icons.py <Skill.bin> <skill_strings.pkl> <files.txt> <skill_names.json> <out.json>"""
import bisect, json, pickle, re, sys
skill_bin, strings_pkl, files, names_path, out = sys.argv[1:6]
d = open(skill_bin, 'rb').read()
ss = pickle.load(open(strings_pkl, 'rb'))
tex = {l.strip().rsplit('/', 1)[-1][:-7].lower(): l.strip().rsplit('/', 1)[-1][:-7] for l in open(files) if 'Resource/Texture/Skill/' in l and l.strip().endswith('.uasset')}
icons = [(m.start(), m.group(1).decode()) for m in re.finditer(rb'Skill/(ICON_[A-Za-z0-9_]+)\x00', d)]
strs = sorted((o, int(t.rsplit('_', 1)[1])) for o, t, e in ss if re.match(r'STR_SKILL_PC_[A-Z]+_\d+$', t))
offs = [s[0] for s in strs]
by_id = {}
for off, ic in icons:
    i = bisect.bisect_right(offs, off) - 1
    if i >= 0 and ic.lower() in tex:
        by_id.setdefault(strs[i][1], tex[ic.lower()])
names = json.load(open(names_path))
res = {}
for k in names:
    sid = int(k)
    if sid % 10000:
        continue
    # the base row first; else any variant row of the same skill
    ic = by_id.get(sid) or next((by_id[v] for v in range(sid + 10, sid + 400, 10) if v in by_id), None)
    if ic:
        res[k] = ic
json.dump(res, open(out, 'w'), separators=(',', ':'))
print(len(res), 'skills with an icon')
