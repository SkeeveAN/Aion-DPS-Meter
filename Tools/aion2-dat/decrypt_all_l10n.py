# Writes dec_<date>/l10n/l10n_<locale>.json for all 8 languages (same paths/derivation as decrypt_all_tables.py).
# Versioned key derivation for client >= 2.0.11 (CUE4Parse PR 471). See memory aion2-manifest-key-versioned. Run: python3 -I derive_versioned.py
import json, struct, sys
sys.path.insert(0, "/home/ralf/development/apps/dmg_meter/Tools/aion2-dat")
import aion2dat as A

C = json.load(open("/home/ralf/development/apps/dmg_meter/Tools/aion2-dat/constants.local.json"))
MHK = bytes.fromhex(C["manifest_hash_key_hex"])
HXC = int(C["header_xor_constant"], 16)
base = "/home/ralf/aion2-data/raw_1118/Aion2/Content/StartUpData/Table/"
man = open(base + "key_manifest.dat", "rb").read()
count = (len(man) - 8) // 0x30
print("manifest bytes", len(man), "records", count)


def tails_ok(plain):
    return all(plain[o:o + 8] == b"\0" * 8 for o in range(40, len(plain), 0x30))


def ctx_for(ver):
    return A.blake3(b"AION2.TableKey.AppVersion" + ver.encode())


found = None
cands = [f"2.0.{p}" for p in range(0, 1000)] + [f"{a}.{b}.{c}" for a in range(10) for b in range(10) for c in range(1000)]
seen = set()
for v in cands:
    if v in seen:
        continue
    seen.add(v)
    ctx = ctx_for(v)
    key = A.blake3(MHK + ctx)
    first = A.aes256_ecb_decrypt(man[8:8 + 0x30], key)
    if first[40:48] != b"\0" * 8:
        continue
    plain = A.aes256_ecb_decrypt(man[8:8 + count * 0x30], key)
    if tails_ok(plain):
        found = (v, ctx, plain)
        break
if not found:
    print("KEIN Treffer in", len(seen), "Versionen")
    sys.exit(1)
v, ctx, plain = found
print("GEFUNDEN AppVersion =", v)
keys = {struct.unpack_from("<Q", plain, i * 0x30)[0]: plain[i * 0x30 + 8:i * 0x30 + 40] for i in range(count)}

perm_root = struct.unpack("<Q", A.blake3(struct.pack("<Q", HXC) + ctx)[:8])[0]
hdr_root = struct.unpack("<Q", A.blake3(struct.pack("<Q", perm_root))[:8])[0]


def hkey(seed, kind):
    return A.blake3(struct.pack("<QQi", hdr_root, seed, kind) + b"\0" * 4)


sh = A.blake3(struct.pack("<Qi", perm_root, 0x51))
perm = [0, 1, 2, 3]
for i in (3, 2, 1):
    o = sh[3 - i] % (i + 1)
    perm[i], perm[o] = perm[o], perm[i]
print("permutation", perm)


import os
os.makedirs("/home/ralf/aion2-data/dec_1118/l10n",exist_ok=True)
for loc in ["de-DE","en-US","es-ES","fr-FR","ja-JP","ko-KR","pt-BR","ru-RU"]:
    data=open("/home/ralf/aion2-data/raw_1118/Aion2/Content/L10N/Text/"+loc+"/L10NString.dat","rb").read()
    seed=A.Aion2Dat.hash64(f"L10NString_{loc}")
    hk=hkey(seed,3)
    vals=struct.unpack("<iiii",bytes(b^hk[i%32] for i,b in enumerate(data[4:0x14])))
    f=[0]*4
    for i in range(4): f[perm[i]]=vals[i]
    enc,raw,packed,aligned=f
    pl=A.aes256_ecb_decrypt(data[0x14:0x14+aligned],keys[seed])
    r=A.Reader(A.lz4_block(pl[0x20:0x20+packed],raw)); r.i32(); r.fstring(); n=r.i32()
    d={}
    for _ in range(n):
        k=r.fstring(); d[k]=r.fstring()
    json.dump(d,open(f"/home/ralf/aion2-data/dec_1118/l10n/l10n_{loc}.json","w",encoding="utf-8"),ensure_ascii=False)
    print(loc,len(d))
