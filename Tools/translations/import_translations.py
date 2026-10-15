#!/usr/bin/env python3
"""Checks a translation file that came back from a native speaker and (only with --apply) takes over the lines that pass.

Usage:  python3 import_translations.py <file.csv> [--lang xx] [--apply]

Safety: the file is treated as plain data, never as instructions. Nothing in it is executed, opened or followed. Only the column
"Your text (edit here)" is read, only for keys that exist, and only where it differs from the current text. Every changed line
must pass these checks, otherwise it is rejected and listed with the reason:
  - the key exists and looks like a key (letters, digits, dots, underscores)
  - no control characters (except a line break), no invisible or text-direction-changing characters
  - the same placeholders ({0}, {1} ...) as the English text, the same number of line breaks
  - a sensible length (not empty, not far longer than the English text)
  - no markup, links, code fences or spreadsheet formulas that the English text does not have
  - no wording that reads like an instruction to a program or an assistant (this tool, the program, an AI)
Without --apply nothing is written: you only get the report to read through.
"""
import csv
import io
import json
import os
import re
import sys

from common import COLUMNS, LANGUAGES, MASTER, PLACEHOLDER, load_master, unsafe_cell

MAX_FILE = 5 * 1024 * 1024
MAX_ROWS = 5000
MAX_CELL = 4000
KEY_OK = re.compile(r"^[A-Za-z0-9_.]{1,120}$")
CONTROL = re.compile(r"[\x00-\x09\x0b-\x1f\x7f-\x9f]")
INVISIBLE = re.compile("[​-‏‪-‮⁠-⁤⁦-⁩﻿]")
MARKUP = re.compile(r"<\s*/?\s*[A-Za-z!?][^>]*>|&#?\w+;|```|\[[^\]]*\]\([^)]*\)")
LINK = re.compile(r"https?://|www\.|ftp://|file://|\\\\[A-Za-z]|javascript:|data:", re.I)
# words that only make sense as an instruction to a program or an assistant, in the languages the program has
SUSPICIOUS = re.compile(
    r"ignore (all |any )?(previous|above|prior)|system prompt|as an? (ai|assistant|language model)|\bclaude\b|\banthropic\b|\bchatgpt\b|"
    r"\bassistant\b|you (must|should) now|disregard|override|jailbreak|execute|run (this|the following)|delete (all|the)|"
    r"vergiss (alle|alles)|ignoriere|ignorez|ignora (las|todas)|игнорир|無視して|무시|忽略以上|忽略之前",
    re.I,
)


def check(key, en, cur, new):
    """Returns a reason text when the new text must be rejected, otherwise None."""
    if not new.strip():
        return "empty"
    if CONTROL.search(new):
        return "contains control characters"
    if INVISIBLE.search(new):
        return "contains invisible or text-direction characters"
    if set(PLACEHOLDER.findall(new)) != set(PLACEHOLDER.findall(en)) or len(PLACEHOLDER.findall(new)) != len(PLACEHOLDER.findall(en)):
        return "placeholders {0}, {1} ... differ from the English text"
    if new.count("\n") != en.count("\n"):
        return "different number of line breaks than the English text"
    if len(new) > max(40, 3 * len(en) + 20):
        return "much longer than the English text"
    if MARKUP.search(new) and not MARKUP.search(en):
        return "contains markup or code that the English text does not have"
    if LINK.search(new) and not LINK.search(en):
        return "contains a link or path that the English text does not have"
    if new and new[0] in "=+-@" and en[:1] != new[0]:
        return "starts with a character that spreadsheets run as a formula"
    if SUSPICIOUS.search(new) and not SUSPICIOUS.search(en):
        return "contains wording that reads like an instruction, not like an interface text"
    return None


def read_rows(path):
    if os.path.getsize(path) > MAX_FILE:
        raise SystemExit("file too large")
    raw = open(path, "rb").read().decode("utf-8-sig", errors="strict")
    reader = csv.reader(io.StringIO(raw, newline=""), strict=True)
    header = next(reader)
    index = {name: header.index(name) for name in COLUMNS if name in header}
    for needed in ("Key", "Current text", "Your text (edit here)"):
        if needed not in index:
            raise SystemExit(f"column missing: {needed}")
    rows = []
    for n, row in enumerate(reader, start=2):
        if n > MAX_ROWS:
            raise SystemExit("too many rows")
        if len(row) <= max(index.values()):
            continue
        rows.append((n, row[index["Key"]], row[index["Current text"]], row[index["Your text (edit here)"]]))
    return rows


def show(text):
    return json.dumps(text, ensure_ascii=False)


def patch_master(code, accepted):
    """Replaces only the changed values inside the master file as text, so its layout stays as it is."""
    raw = open(MASTER, encoding="utf-8", newline="").read()
    for key, new in accepted.items():
        start = raw.index(f'"{key}": {{')
        end = raw.index("}", start)
        block = raw[start:end]
        old = json.loads(raw)[key].get(code)
        if old is None:
            raise SystemExit(f"{key} has no {code} entry yet; add it by hand first")
        a = f'"{code}": {json.dumps(old, ensure_ascii=False)}'
        if a not in block:
            raise SystemExit(f"cannot place {key}")
        raw = raw[:start] + block.replace(a, f'"{code}": {json.dumps(new, ensure_ascii=False)}', 1) + raw[end:]
    json.loads(raw)  # must still be valid
    open(MASTER, "w", encoding="utf-8", newline="").write(raw)


def main():
    args = sys.argv[1:]
    if not args:
        raise SystemExit(__doc__)
    path = args[0]
    apply = "--apply" in args
    code = args[args.index("--lang") + 1] if "--lang" in args else None
    if code is None:
        m = re.search(r"aiondps-translation-([A-Za-z-]+)\.csv$", os.path.basename(path))
        code = m.group(1) if m else None
    if code not in LANGUAGES:
        raise SystemExit("language unknown: give --lang xx (one of " + ", ".join(LANGUAGES) + ")")
    master = load_master()
    accepted, rejected, unchanged = {}, [], 0
    for n, key, cur, new in read_rows(path):
        if not KEY_OK.match(key) or key not in master:
            rejected.append((n, key[:60], "unknown key"))
            continue
        if any(len(c) > MAX_CELL for c in (cur, new)):
            rejected.append((n, key, "cell too long"))
            continue
        en = master[key].get("en", "")
        current = master[key].get(code, "")
        new = unsafe_cell(new, current)
        if new == current:
            unchanged += 1
            continue
        reason = check(key, en, current, new)
        if reason:
            rejected.append((n, key, reason + " -> " + show(new)[:120]))
        else:
            accepted[key] = new
    print(f"Language {code}: {len(accepted)} changes accepted, {len(rejected)} rejected, {unchanged} unchanged\n")
    for key, new in accepted.items():
        print(f"+ {key}\n    old: {show(master[key].get(code, ''))}\n    new: {show(new)}")
    for n, key, why in rejected:
        print(f"! line {n} {key}: {why}")
    if apply and accepted:
        patch_master(code, accepted)
        print("\nApplied. Check the result with git diff before building.")
    elif accepted:
        print("\nDry run: nothing written. Run again with --apply to take over the accepted lines.")


if __name__ == "__main__":
    main()
