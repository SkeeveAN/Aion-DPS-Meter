"""Own reader for AION 2 .dat files (L10N text tables), written from the public description of the
format. Reads local files only. Nothing here is imported into the meter or the website.

Layout (clients before 2.0.11 / 11.11.2026; the versioned changes are described at class Aion2Dat):
  key_manifest.dat : 8 bytes header, then N * 48 bytes, AES-256-ECB encrypted with blake3(MANIFEST_HASH_KEY).
                     Each decrypted entry: u64 seed, 32 byte AES key, 8 bytes unused.
  L10NString.dat   : i32 version (2); 16 header bytes XOR-ed with a 32 byte key derived from blake3;
                     header = i32 packed, i32 encryptionType (2), i32 aligned, i32 raw;
                     then `aligned` bytes AES-256-ECB with manifest[seed]; the decrypted block holds a
                     0x20 byte prefix, then `packed` bytes of an LZ4 block that expands to `raw` bytes;
                     those are: i32 1, FString namespace, i32 count, count * (FString key, FString value).
"""
import struct
import subprocess
import sys

# ---- BLAKE3 for inputs up to one chunk (1024 bytes): all that this format ever hashes ----------
_IV = [0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A, 0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19]
_PERM = [2, 6, 3, 10, 7, 0, 4, 13, 1, 11, 12, 5, 9, 14, 15, 8]
_CHUNK_START, _CHUNK_END, _ROOT = 1, 2, 8
_M = 0xFFFFFFFF


def _rotr(x, n):
    return ((x >> n) | (x << (32 - n))) & _M


def _g(s, a, b, c, d, mx, my):
    s[a] = (s[a] + s[b] + mx) & _M
    s[d] = _rotr(s[d] ^ s[a], 16)
    s[c] = (s[c] + s[d]) & _M
    s[b] = _rotr(s[b] ^ s[c], 12)
    s[a] = (s[a] + s[b] + my) & _M
    s[d] = _rotr(s[d] ^ s[a], 8)
    s[c] = (s[c] + s[d]) & _M
    s[b] = _rotr(s[b] ^ s[c], 7)


def _compress(cv, block, counter, block_len, flags):
    s = list(cv) + _IV[:4] + [counter & _M, (counter >> 32) & _M, block_len, flags]
    m = list(block)
    for r in range(7):
        _g(s, 0, 4, 8, 12, m[0], m[1])
        _g(s, 1, 5, 9, 13, m[2], m[3])
        _g(s, 2, 6, 10, 14, m[4], m[5])
        _g(s, 3, 7, 11, 15, m[6], m[7])
        _g(s, 0, 5, 10, 15, m[8], m[9])
        _g(s, 1, 6, 11, 12, m[10], m[11])
        _g(s, 2, 7, 8, 13, m[12], m[13])
        _g(s, 3, 4, 9, 14, m[14], m[15])
        if r < 6:
            m = [m[i] for i in _PERM]
    return [s[i] ^ s[i + 8] for i in range(8)] + [s[i + 8] ^ cv[i] for i in range(8)]


def blake3(data: bytes) -> bytes:
    if len(data) > 1024:
        raise ValueError("only single-chunk inputs are supported")
    blocks = [data[i:i + 64] for i in range(0, len(data), 64)] or [b""]
    cv = list(_IV)
    for i, blk in enumerate(blocks):
        flags = (_CHUNK_START if i == 0 else 0) | ((_CHUNK_END | _ROOT) if i == len(blocks) - 1 else 0)
        words = list(struct.unpack("<16I", blk.ljust(64, b"\0")))
        out = _compress(cv, words, 0, len(blk), flags)
        cv = out[:8]
    return struct.pack("<8I", *out[:8])


def _selftest():
    assert blake3(b"").hex() == "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262", "blake3 empty"
    assert blake3(b"abc").hex() == "6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85", "blake3 abc"


# ---- helpers ----------------------------------------------------------------------------------
def aes256_ecb_decrypt(data: bytes, key: bytes) -> bytes:
    p = subprocess.run(["openssl", "enc", "-aes-256-ecb", "-d", "-K", key.hex(), "-nopad"],
                       input=data, capture_output=True, check=True)
    return p.stdout


def lz4_block(src: bytes, raw_size: int) -> bytes:
    out = bytearray()
    i = 0
    n = len(src)
    while i < n:
        tok = src[i]
        i += 1
        lit = tok >> 4
        if lit == 15:
            while True:
                b = src[i]
                i += 1
                lit += b
                if b != 255:
                    break
        out += src[i:i + lit]
        i += lit
        if i >= n:
            break
        off = src[i] | (src[i + 1] << 8)
        i += 2
        ml = tok & 15
        if ml == 15:
            while True:
                b = src[i]
                i += 1
                ml += b
                if b != 255:
                    break
        ml += 4
        start = len(out) - off
        if off >= ml:
            out += out[start:start + ml]
        else:
            for _ in range(ml):
                out.append(out[-off])
    if len(out) != raw_size:
        raise ValueError(f"LZ4 produced {len(out)} bytes, header says {raw_size}")
    return bytes(out)


class Reader:
    def __init__(self, data):
        self.d, self.p = data, 0

    def i32(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def fstring(self):
        n = self.i32()
        if n == 0:
            return ""
        if n > 0:
            s = self.d[self.p:self.p + n]
            self.p += n
            return s[:-1].decode("utf-8", "replace")
        s = self.d[self.p:self.p - 2 * n]
        self.p += -2 * n
        return s[:-2].decode("utf-16-le", "replace")


class Aion2Dat:
    """Works with both client generations: the plain manifest (before 11.11.2026) and the versioned one
    (client 2.0.11 and later, CUE4Parse PR 471): manifest key = blake3(manifest_hash_key + ctx) with
    ctx = blake3("AION2.TableKey.AppVersion" + version); the version is searched, see load_manifest."""

    def __init__(self, manifest_hash_key: bytes, header_xor_constant: int):
        _selftest()
        self.manifest_hash_key = manifest_hash_key
        self.header_xor_constant = header_xor_constant
        self.header_key_root = struct.unpack("<Q", blake3(struct.pack("<Q", header_xor_constant))[:8])[0]
        self.perm = [0, 1, 2, 3]   # position in the file -> field; identity before the versioned client
        self.version = None        # AppVersion found by load_manifest, None = plain manifest
        self.keys = {}

    @staticmethod
    def hash64(text: str) -> int:
        return struct.unpack("<Q", blake3(text.encode("utf-8"))[:8])[0]

    def header_key(self, seed: int, kind: int) -> bytes:
        return blake3(struct.pack("<QQi", self.header_key_root, seed, kind) + b"\0" * 4)

    @staticmethod
    def _tails_ok(plain: bytes) -> bool:
        return all(plain[o:o + 8] == b"\0" * 8 for o in range(40, len(plain), 0x30))

    def _use_version(self, ctx: bytes):
        perm_root = struct.unpack("<Q", blake3(struct.pack("<Q", self.header_xor_constant) + ctx)[:8])[0]
        self.header_key_root = struct.unpack("<Q", blake3(struct.pack("<Q", perm_root))[:8])[0]
        shuffle = blake3(struct.pack("<Qi", perm_root, 0x51))
        perm = [0, 1, 2, 3]
        for i in (3, 2, 1):
            o = shuffle[3 - i] % (i + 1)
            perm[i], perm[o] = perm[o], perm[i]
        self.perm = perm

    def load_manifest(self, data: bytes):
        if len(data) < 0x38 or (len(data) - 8) % 0x30:
            raise ValueError("unexpected key_manifest.dat size")
        count = (len(data) - 8) // 0x30
        body = data[8:8 + count * 0x30]
        plain = aes256_ecb_decrypt(body, blake3(self.manifest_hash_key))
        if not self._tails_ok(plain):
            plain = None
            # the version is not in the file: try numeric X.Y.Z, 2.0.x first; a wrong key leaves garbage
            # in the reserved 8 bytes of every record
            order = [(2, 0, p) for p in range(1000)] + [(a, b, c) for a in range(10) for b in range(10) for c in range(1000)]
            for a, b, c in order:
                version = f"{a}.{b}.{c}"
                ctx = blake3(b"AION2.TableKey.AppVersion" + version.encode())
                key = blake3(self.manifest_hash_key + ctx)
                if aes256_ecb_decrypt(body[:0x30], key)[40:48] != b"\0" * 8:
                    continue
                cand = aes256_ecb_decrypt(body, key)
                if self._tails_ok(cand):
                    plain, self.version = cand, version
                    self._use_version(ctx)
                    break
            if plain is None:
                raise ValueError("key_manifest.dat: neither the plain key nor any versioned key (0.0.0-9.9.999) fits")
        for i in range(count):
            e = plain[i * 0x30:(i + 1) * 0x30]
            self.keys[struct.unpack_from("<Q", e, 0)[0]] = e[8:40]
        return count

    def _fields(self, data: bytes, off: int, seed: int, kind: int):
        """The four header ints at data[off:off+16] as (type, raw, packed, aligned)."""
        hk = self.header_key(seed, kind)
        v = struct.unpack("<iiii", bytes(b ^ hk[i % len(hk)] for i, b in enumerate(data[off:off + 16])))
        if self.version is None:   # old order on disk: packed, type, aligned, raw
            return v[1], v[3], v[0], v[2]
        f = [0] * 4
        for i in range(4):
            f[self.perm[i]] = v[i]
        return f[0], f[1], f[2], f[3]

    def read_l10n(self, data: bytes, locale: str):
        if len(data) < 0x14 or struct.unpack_from("<i", data, 0)[0] != 2:
            raise ValueError("not a version 2 L10N container")
        seed = self.hash64(f"L10NString_{locale}")
        enc_type, raw, packed, aligned = self._fields(data, 4, seed, 3)
        info = dict(seed=seed, packed=packed, enc_type=enc_type, aligned=aligned, raw=raw)
        if enc_type != 2 or not (0 < packed <= aligned <= len(data)) or raw <= 0:
            raise ValueError(f"header does not look right: {info}")
        if seed not in self.keys:
            raise ValueError(f"seed {seed:#x} not in the key manifest: {info}")
        plain = aes256_ecb_decrypt(data[0x14:0x14 + aligned], self.keys[seed])
        body = lz4_block(plain[0x20:0x20 + packed], raw)
        r = Reader(body)
        if r.i32() != 1:
            raise ValueError("unexpected L10N table version")
        namespace = r.fstring()
        count = r.i32()
        entries = {}
        for _ in range(count):
            k = r.fstring()
            entries[k] = r.fstring()
        return info, namespace, entries

    def read_table(self, data: bytes, name: str) -> bytes:
        """Decrypts one version-13 data table (AION2/Content/Data/Table/<name>.dat) to its raw body."""
        if struct.unpack_from("<i", data, 0)[0] != 13:
            raise ValueError(f"{name}: not a version 13 table container")
        seed = self.hash64(name)
        if seed not in self.keys:
            raise ValueError(f"{name}: no key in the manifest")
        key = self.keys[seed]
        enc_type, raw, packed, aligned = self._fields(data, 4, seed, 1)
        if enc_type == 2 and 0 < packed <= aligned - 0x20 and aligned <= len(data) - 0x14 and raw > 0:
            plain = aes256_ecb_decrypt(data[0x14:0x14 + aligned], key)
            return lz4_block(plain[0x20:0x20 + packed], raw)
        # big tables: AES-CTR style stream (CUE4Parse DecryptStreamDataTable): header kind 2, size + type 3, payload at 12
        hk2 = self.header_key(seed, 2)
        size, et2 = struct.unpack("<ii", bytes(b ^ hk2[i % len(hk2)] for i, b in enumerate(data[4:12])))
        if et2 != 3 or size != len(data) - 12:
            raise ValueError(f"{name}: unknown container {enc_type} / {et2}")
        import array
        from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
        n0 = struct.unpack("<Q", blake3(key + b"nonc")[:8])[0]
        ctr = array.array("Q")
        for i in range((size + 15) // 16):
            ctr.append(n0)
            ctr.append(i)
        ks = Cipher(algorithms.AES(key), modes.ECB()).encryptor().update(ctr.tobytes())[:size]
        return (int.from_bytes(data[12:], "little") ^ int.from_bytes(ks, "little")).to_bytes(size, "little")
