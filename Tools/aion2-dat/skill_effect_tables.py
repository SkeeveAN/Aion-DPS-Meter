"""Reads the value lists of the skill effect tables, to fill the numbers into the client's skill texts.

The texts hold placeholders such as {se:1102002011:effect_value14:divide100} (SkillEffect, value 14 of the row 1102002011, divided
by 100) and {abe:1125002014:value02:divide100} (SkillAbnormalEffect). Both tables are the decrypted `.dat` files (`dectable.py` of
this folder, or the Extract tool of Tools/aion2-icons: `table <out> SkillEffect SkillAbnormalEffect`).

Row layout, found by the schema of the game's .usmap (SkillEffectTableItem: ID, SkillEffectGroupId, EffectType, hit effects,
..., EffectValueList = array of strings) and checked against the numbers the game's tooltips show:
  SkillEffect:         [id u32][group u32 = id / 10] [XOR-ed UTF-16 strings and small ints ...] 'EHideFloaterType::..'
                       [aggro ratio i32][aggro absolute i32][count i32] count x [len i32, ASCII + NUL]
  SkillAbnormalEffect: [id u32][group u32] [XOR-ed strings ...] 'EAbnormalEffectType::..' then, a few bytes later, [count i32] count x string
Values are 1-based in the placeholders (effect_value14 = the 14th entry); a pair like "100, 100" is (min, max) and the first is used.
"""
import re
import struct

KEY = bytes([0x25, 0, 0xA8, 0, 0x7E, 0, 0x91, 0])


def _xor_string(d: bytes, p: int):
    """An XOR-ed UTF-16 FString at p: (text, end) or (None, p)."""
    length = struct.unpack_from("<i", d, p)[0]
    if not -300 < length < -1:
        return None, p
    raw = bytearray(d[p + 4 : p + 4 + (-length) * 2])
    for i in range(len(raw)):
        raw[i] ^= KEY[i % 8]
    try:
        return raw.decode("utf-16le")[:-1], p + 4 + (-length) * 2
    except UnicodeDecodeError:
        return None, p


def _ascii_string(d: bytes, p: int):
    length = struct.unpack_from("<i", d, p)[0]
    if not 0 < length < 400 or p + 4 + length > len(d) or d[p + 3 + length] != 0:
        return None, p
    raw = d[p + 4 : p + 3 + length]
    if any(c < 32 or c > 126 for c in raw):
        return None, p
    return raw.decode("ascii"), p + 4 + length


def _value_list(d: bytes, p: int):
    n = struct.unpack_from("<i", d, p)[0]
    if not 1 <= n <= 60:
        return None
    p += 4
    values = []
    for _ in range(n):
        s, p = _ascii_string(d, p)
        if s is None:
            return None
        values.append(s)
    return values


def _enum_marker(prefix: str) -> bytes:
    enc = bytearray(prefix.encode("utf-16le"))
    for i in range(len(enc)):
        enc[i] ^= KEY[i % 8]
    return bytes(enc)


def read_rows(path: str, enum_prefix: str, stop_prefix: str | None):
    """{row id: [values]} of a table. enum_prefix starts the first XOR-ed enum of a row; stop_prefix (SkillEffect only) is the enum after which
    the value list follows directly; without it the list is searched within 64 bytes after the first enum string."""
    d = open(path, "rb").read()
    marker = _enum_marker(enum_prefix)
    rows: dict[int, list[str]] = {}
    pos = 0
    while True:
        j = d.find(marker, pos)
        if j < 0:
            break
        pos = j + 1
        start = j - 4
        if start < 12 or not -100 < struct.unpack_from("<i", d, start)[0] < -len(enum_prefix):
            continue
        row_id = None
        for back in range(8, 90):
            a, b = struct.unpack_from("<II", d, start - back)
            if a >= 1_000_000 and b == a // 10:
                row_id = a
                break
        if row_id is None:
            continue
        p = start
        if stop_prefix is not None:
            found = False
            for _ in range(16):
                text, q = _xor_string(d, p)
                if text is not None:
                    p = q
                    if text.startswith(stop_prefix):
                        found = True
                        break
                    continue
                for skip in (1, 4, 5):
                    text, q = _xor_string(d, p + skip)
                    if text is not None:
                        p += skip
                        break
                else:
                    break
            if not found:
                continue
            p += 8  # aggro ratio, aggro absolute
            values = _value_list(d, p)
        else:
            _, p = _xor_string(d, p)
            values = None
            for off in range(0, 64):
                values = _value_list(d, p + off)
                if values is not None:
                    break
        if values is not None:
            rows.setdefault(row_id, values)
    return rows


def load_effects(se_path: str, abe_path: str):
    return (
        read_rows(se_path, "ESkillEffectType::", "EHideFloaterType"),
        read_rows(abe_path, "EAbnormalEffectType::", None),
    )


_NUMBER = re.compile(r"-?\d+(?:\.\d+)?")


def _first_number(value: str):
    m = _NUMBER.search(value)
    return float(m.group(0)) if m else None


def _shown(x: float, comma: bool) -> str:
    text = f"{x:.2f}".rstrip("0").rstrip(".")
    return text.replace(".", ",") if comma else text


SECONDS = {"de": " Sek.", "en": "s", "es": " s", "fr": " s", "ja": "秒", "ko": "초", "pt": "s", "ru": " сек."}
COMMA = {"de", "es", "fr", "pt", "ru"}
_PLACEHOLDER = re.compile(r"\{(se_abe|se_dmg|se|abe|sef):([^}]*)\}")


def fill(text: str, lang: str, se_rows, abe_rows):
    """Replaces the placeholders it can resolve by their numbers; returns (text, unresolved count)."""
    missing = 0

    def one(m):
        nonlocal missing
        kind, args = m.group(1), m.group(2).split(":")
        raw = None
        try:
            if kind == "se" and len(args) >= 2:
                row, field, fmt = int(args[0]), args[1], args[2] if len(args) > 2 else ""
                raw = se_rows[row][int(field.removeprefix("effect_value")) - 1]
            elif kind == "abe" and len(args) >= 2:
                row, field, fmt = int(args[0]), args[1], args[2] if len(args) > 2 else ""
                raw = abe_rows[row][int(field.removeprefix("value")) - 1]
            elif kind == "se_abe" and len(args) >= 3:
                row, field, fmt = int(args[1]), args[2], args[3] if len(args) > 3 else ""
                raw = abe_rows[row][int(field.removeprefix("value")) - 1]
        except (KeyError, IndexError, ValueError):
            raw = None
        number = _first_number(raw) if raw is not None else None
        if number is None:
            missing += 1
            return ""
        comma = lang in COMMA
        if fmt == "time":
            return _shown(number / 1000, comma) + SECONDS[lang]
        if fmt.startswith("divide100"):
            number /= 100
        elif fmt.startswith("divide10"):
            number /= 10
        if fmt.endswith("abs"):
            number = abs(number)
        return _shown(number, comma)

    return _PLACEHOLDER.sub(one, text), missing
