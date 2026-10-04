import pickle,re,json,collections
ss=pickle.load(open('skill_strings.pkl','rb'))
tex=set(l.strip().rsplit('/',1)[-1][:-7] for l in open('/mnt/d/files.txt') if 'Resource/Texture/Skill/' in l and l.strip().endswith('.uasset'))
texl={t.lower():t for t in tex}
CODE={'Gladiator':'GL','Assassin':'AS','Ranger':'RA','Sorcerer':'SO','Templar':'TE','Chanter':'CH','Cleric':'CL','Elementalist':'EL','Gunslinger':'GT','Fighter':'CO'}
groups=collections.defaultdict(lambda:{'tok':None,'passive':False}); cur=None
for o,t,e in ss:
    m=re.match(r'STR_SKILL_PC_([A-Z]+)_(\d+)$',t)
    if m: cur=groups[int(m.group(2))//10000]; continue
    if cur is None: continue
    if t=='ESkillType::Passive': cur['passive']=True
    if cur['tok'] is None and re.match(r'[A-Za-z]+_Skill\d{3}(_|$)',t): cur['tok']=t
names=json.load(open('/home/ralf/development/apps/dmg_meter/Backend/src/data/aion2/skill_names.json'))
res={};miss=[]
for k in names:
    sid=int(k)
    if sid%10000: continue
    g=groups.get(sid//10000)
    if not g or not g['tok']: miss.append((sid,'notok')); continue
    cls,num=g['tok'].split('_Skill'); num=num[:3]; code=CODE.get(cls)
    cands=[f'ICON_{code}_SKILL_Passive_{num}',f'ICON_{code}_SKILL_{num}'] if g['passive'] else [f'ICON_{code}_SKILL_{num}',f'ICON_{code}_SKILL_Passive_{num}']
    hit=next((texl[c.lower()] for c in cands if c.lower() in texl),None)
    if hit: res[k]=hit
    else: miss.append((sid,g['tok'],g['passive']))
print(len(res),len(miss),miss[:15])
json.dump(res,open('skill_icons.json','w'))
