"""Own reader for AION 2 .dat files (L10N text tables), written from the public description of the
format. Reads local files only. Nothing here is imported into the meter or the website.

Layout (current client):
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
    def __init__(self, manifest_hash_key: bytes, header_xor_constant: int):
        _selftest()
        self.manifest_hash_key = manifest_hash_key
        root = struct.unpack("<Q", blake3(struct.pack("<Q", header_xor_constant))[:8])[0]
        self.header_key_root = root
        self.keys = {}

    @staticmethod
    def hash64(text: str) -> int:
        return struct.unpack("<Q", blake3(text.encode("utf-8"))[:8])[0]

    def header_key(self, seed: int, kind: int) -> bytes:
        return blake3(struct.pack("<QQi", self.header_key_root, seed, kind) + b"\0" * 4)

    def load_manifest(self, data: bytes):
        if len(data) < 8 or (len(data) - 8) % 0x30:
            raise ValueError("unexpected key_manifest.dat size")
        count = (len(data) - 8) // 0x30
        plain = aes256_ecb_decrypt(data[8:8 + count * 0x30], blake3(self.manifest_hash_key))
        for i in range(count):
            e = plain[i * 0x30:(i + 1) * 0x30]
            self.keys[struct.unpack_from("<Q", e, 0)[0]] = e[8:40]
        return count

    def read_l10n(self, data: bytes, locale: str):
        if len(data) < 0x14 or struct.unpack_from("<i", data, 0)[0] != 2:
            raise ValueError("not a version 2 L10N container")
        seed = self.hash64(f"L10NString_{locale}")
        hk = self.header_key(seed, 3)
        head = bytes(b ^ hk[i % len(hk)] for i, b in enumerate(data[4:0x14]))
        packed, enc_type, aligned, raw = struct.unpack("<iiii", head)
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
