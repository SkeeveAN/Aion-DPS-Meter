"""Reads every NPC id and its name key out of the decrypted NpcData table and joins the names in all languages.
Usage: python3 build_npcs.py <NpcData.bin from dectable.py> <dir with l10n_<locale>.json from extract_l10n.py> <out.json>
Rows are [i32 npc id][internal name][STR_ text key]...; the texts are String_<key>_body. Client/assets/aion2/npcs/npc_names.json
keeps English, German, French, Spanish and Russian of the result."""
import sys,struct,json,os
d=open(sys.argv[1],'rb').read(); L=sys.argv[2]; outp=sys.argv[3]
key=bytes([0x25,0,0xa8,0,0x7e,0,0x91,0])
marker=bytes([0x76,0,0xfc,0,0x2c,0,0xce,0])
def dec(p,n):
    # n counts the terminating null, which the file leaves unXORed: decode the n-1 characters only
    raw=d[p:p+2*(n-1)]
    return bytes(b^key[j%8] for j,b in enumerate(raw)).decode('utf-16le','replace')
rows={}
pos=0
while True:
    q=d.find(marker,pos)
    if q<0: break
    pos=q+1
    if q<12: continue
    len2=struct.unpack_from('<i',d,q-4)[0]
    if not (-120<len2<-6): continue
    key2=dec(q,-len2)
    if not key2.startswith('STR_') or not key2.isascii(): continue
    found=None
    for L1 in range(2,100):
        p1=q-4-2*L1-4
        if p1<4: break
        if struct.unpack_from('<i',d,p1)[0]!=-L1: continue
        n1=dec(p1+4,L1)
        if n1.isascii() and n1.isprintable():
            found=(p1,n1); break
    if not found: continue
    p1,n1=found
    npc=struct.unpack_from('<i',d,p1-4)[0]
    if 100000<=npc<100000000:
        rows[npc]=key2
print('rows',len(rows))
langs={'en':'en-US','de':'de-DE','fr':'fr-FR','es':'es-ES','ru':'ru-RU','pt':'pt-BR','ja':'ja-JP','ko':'ko-KR'}
L10={c:json.load(open(f'{L}/l10n_{f}.json',encoding='utf-8')) for c,f in langs.items()}
out={}
miss=0
for npc,k in sorted(rows.items()):
    tk=f'String_{k}_body'
    names={c:t[tk] for c,t in L10.items() if tk in t and t[tk] and t[tk]!='???'}
    if names.get('en'):
        out[str(npc)]=names
    else: miss+=1
print('named',len(out),'unnamed',miss)
for t in ('2300709','2330709','2300104','2300171'): print(t,out.get(t,{}).get('en'),'|',out.get(t,{}).get('de'))
json.dump(out,open(outp,'w',encoding='utf-8'),ensure_ascii=False)
print(os.path.getsize(outp))
