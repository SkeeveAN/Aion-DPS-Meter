# Versioned key derivation for client >= 2.0.11 (CUE4Parse PR 471). See memory aion2-manifest-key-versioned. Run: python3 -I derive_versioned.py
import json, struct, sys
sys.path.insert(0, "/home/ralf/development/apps/dmg_meter/Tools/aion2-dat")
import aion2dat as A

C = json.load(open("/home/ralf/development/apps/dmg_meter/Tools/aion2-dat/constants.local.json"))
MHK = bytes.fromhex(C["manifest_hash_key_hex"])
HXC = int(C["header_xor_constant"], 16)
base = "/home/ralf/aion2-data/raw_1111/Aion2/Content/StartUpData/Table/"
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

for loc in ["en-US", "de-DE"]:
    data = open(base + f"L10N/{loc}/L10NString.dat", "rb").read()
    seed = A.Aion2Dat.hash64(f"L10NString_{loc}")
    hk = hkey(seed, 3)
    head = bytes(b ^ hk[i % 32] for i, b in enumerate(data[4:0x14]))
    vals = struct.unpack("<iiii", head)
    f = [0] * 4
    for i in range(4):
        f[perm[i]] = vals[i]
    enc, raw, packed, aligned = f
    print(loc, "seed in manifest:", seed in keys, dict(enc=enc, raw=raw, packed=packed, aligned=aligned))
    if enc != 2 or seed not in keys:
        continue
    pl = A.aes256_ecb_decrypt(data[0x14:0x14 + aligned], keys[seed])
    body = A.lz4_block(pl[0x20:0x20 + packed], raw)
    r = A.Reader(body)
    print(" ver", r.i32(), "ns", repr(r.fstring()), "count", r.i32())
    print(" first entry:", r.fstring(), "=", r.fstring())

print("---- grosse L10N + Tabellen ----")
import itertools, os
big = "/home/ralf/aion2-data/raw_1111/AION2/Content/Data/Table/"
data = open(big + "L10N/de-DE/L10NString.dat", "rb").read()
seed = A.Aion2Dat.hash64("L10NString_de-DE")
hk = hkey(seed, 3)
vals = struct.unpack("<iiii", bytes(b ^ hk[i % 32] for i, b in enumerate(data[4:0x14])))
f = [0] * 4
for i in range(4): f[perm[i]] = vals[i]
enc, raw, packed, aligned = f
print("big de-DE", dict(enc=enc, raw=raw, packed=packed, aligned=aligned), "seed ok", seed in keys)
if enc == 2 and seed in keys:
    pl = A.aes256_ecb_decrypt(data[0x14:0x14 + aligned], keys[seed])
    r = A.Reader(A.lz4_block(pl[0x20:0x20 + packed], raw)); r.i32(); r.fstring()
    print(" Eintraege:", r.i32(), "|", r.fstring(), "=", r.fstring())

ok = bad = 0
kinds = {}
for fn in sorted(os.listdir(big)):
    if not fn.endswith(".dat"): continue
    d = open(big + fn, "rb").read()
    name = fn[:-4]; seed = A.Aion2Dat.hash64(name)
    if seed not in keys: bad += 1; kinds["kein Seed"] = kinds.get("kein Seed", 0) + 1; continue
    res = None
    # Variante A: kind 1, 16 Byte, 4 Felder permutiert
    hk1 = hkey(seed, 1)
    v = struct.unpack("<iiii", bytes(b ^ hk1[i % 32] for i, b in enumerate(d[4:20])))
    for p in itertools.permutations(range(4)):
        ff = [0]*4
        for i in range(4): ff[p[i]] = v[i]
        e, rw, pk, al = ff   # Feldreihenfolge wie bei L10N
        if e == 2 and 0 < pk <= al - 0x20 and al <= len(d) and rw > 0 and al % 16 == 0:
            res = ("komprimiert", p); break
    if not res:
        hk2 = hkey(seed, 2)
        sz, et = struct.unpack("<ii", bytes(b ^ hk2[i % 32] for i, b in enumerate(d[4:12])))
        if et == 3 and sz == len(d) - 12: res = ("stream", None)
    if res: ok += 1; kinds[res[0] + str(res[1])] = kinds.get(res[0] + str(res[1]), 0) + 1
    else: bad += 1; kinds["unklar"] = kinds.get("unklar", 0) + 1
print("Tabellen erkannt:", ok, "nicht:", bad, kinds)

print("---- strenger Volltest mit fester Permutation ----")
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
import array
outdir = "/home/ralf/aion2-data/dec_1113"
os.makedirs(outdir, exist_ok=True)
good = 0; fails = []
for fn in sorted(os.listdir(big)):
    if not fn.endswith(".dat"): continue
    d = open(big + fn, "rb").read(); name = fn[:-4]; seed = A.Aion2Dat.hash64(name)
    try:
        hk1 = hkey(seed, 1)
        v = struct.unpack("<iiii", bytes(b ^ hk1[i % 32] for i, b in enumerate(d[4:20])))
        ff = [0]*4
        for i in range(4): ff[perm[i]] = v[i]
        e, rw, pk, al = ff
        if e == 2 and 0 < pk <= al - 0x20 and al <= len(d) - 0x14 and rw > 0:
            pl = A.aes256_ecb_decrypt(d[0x14:0x14 + al], keys[seed])
            body = A.lz4_block(pl[0x20:0x20 + pk], rw)
        else:
            hk2 = hkey(seed, 2)
            sz, et = struct.unpack("<ii", bytes(b ^ hk2[i % 32] for i, b in enumerate(d[4:12])))
            assert et == 3 and sz == len(d) - 12, "weder noch"
            key = keys[seed]; nonce = A.blake3(key + b"nonc")[:8]; n0 = struct.unpack("<Q", nonce)[0]
            ctr = array.array("Q")
            for i in range((sz + 15)//16): ctr.append(n0); ctr.append(i)
            ks = Cipher(algorithms.AES(key), modes.ECB()).encryptor().update(ctr.tobytes())[:sz]
            body = (int.from_bytes(d[12:], "little") ^ int.from_bytes(ks, "little")).to_bytes(sz, "little")
        open(os.path.join(outdir, name + ".bin"), "wb").write(body); good += 1
    except Exception as ex:
        fails.append((name, str(ex)[:60]))
print("entschluesselt:", good, "Fehler:", len(fails)); print(fails[:10])
for t in ("NpcData", "Item", "Skill"):
    p = os.path.join(outdir, t + ".bin")
    if os.path.exists(p): print(t, os.path.getsize(p))
