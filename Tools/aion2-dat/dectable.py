"""Decrypts one version-13 data table (AION2/Content/Data/Table/<Name>.dat) to its raw body.
Usage: python3 dectable.py <manifest> <table.dat> <out.bin>   (seed = hash of the table name, header kind 1)"""
import json, os, struct, sys
from aion2dat import Aion2Dat, aes256_ecb_decrypt, lz4_block, blake3
here = os.path.dirname(os.path.abspath(__file__))
c = json.load(open(os.path.join(here, "constants.local.json")))
dat = Aion2Dat(bytes.fromhex(c["manifest_hash_key_hex"]), int(c["header_xor_constant"], 16))
dat.load_manifest(open(sys.argv[1], "rb").read())
d = open(sys.argv[2], "rb").read()
name = os.path.splitext(os.path.basename(sys.argv[2]))[0]
seed = dat.hash64(name)
hk = dat.header_key(seed, 1)
packed, et, aligned, raw = struct.unpack("<iiii", bytes(b ^ hk[i % len(hk)] for i, b in enumerate(d[4:20])))
if et == 2:
    plain = aes256_ecb_decrypt(d[0x14:0x14 + aligned], dat.keys[seed])
    body = lz4_block(plain[0x20:0x20 + packed], raw)
else:
    # big tables: AES-CTR style stream (CUE4Parse DecryptStreamDataTable): header kind 2, size + type 3, payload at 12
    hk2 = dat.header_key(seed, 2)
    size, et2 = struct.unpack("<ii", bytes(b ^ hk2[i % len(hk2)] for i, b in enumerate(d[4:12])))
    if et2 != 3 or size != len(d) - 12:
        raise ValueError(f"{name}: unknown container {et} / {et2}")
    key = dat.keys[seed]
    nonce = blake3(key + b"nonc")[:8]
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    import array
    n = (size + 15) // 16
    n0 = struct.unpack("<Q", nonce)[0]
    ctr = array.array("Q")
    for i in range(n):
        ctr.append(n0); ctr.append(i)
    ks = Cipher(algorithms.AES(key), modes.ECB()).encryptor().update(ctr.tobytes())[:size]
    body = (int.from_bytes(d[12:], "little") ^ int.from_bytes(ks, "little")).to_bytes(size, "little")
open(sys.argv[3], "wb").write(body)
print(name, "ok", len(body))
