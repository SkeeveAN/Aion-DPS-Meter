#!/usr/bin/env python3
"""Writes one spreadsheet file (CSV) per language for native speakers to review and improve.

Usage:  python3 export_translations.py [out dir]     (default: export/ next to this script)

Every file lists all texts of the program with the English source, the German text as a second reference, the current text in
that language and a column to edit. Open it in Excel, LibreOffice or Google Sheets, change what is wrong, save as CSV (UTF-8)
and send it back. Nothing in the file is ever run: import_translations.py reads it as plain data and checks every line.
"""
import csv
import os
import sys

from common import COLUMNS, HERE, LANGUAGES, load_master, notes, safe_cell, where

# texts changed or added recently: speakers are asked to look at these first
REVIEW_FIRST = {
    "Settings.Hotkey.Mode", "Settings.Hotkey.Mode.Tip", "Settings.AutoReset", "Settings.OverlayStyle", "Main.ModeAllCompact", "Main.FilterElyos", "Main.FilterAsmodian", "Main.Scope.Corps",
}

README = """Aion DPS translation file ({english} / {native})
==========================================================

Thank you for helping! Please read this once.

* Open {file} in Excel, LibreOffice Calc or Google Sheets (encoding: UTF-8).
* Column "Current text" is what the program shows now. Column "Your text (edit here)" starts with the same text: change it where
  it sounds wrong, unnatural or is mistranslated. Leave it unchanged where it is fine. Please do not edit any other column.
* Column "Where it appears" tells you what kind of text it is (button, tooltip, explanation, menu ...). Buttons and menu entries
  must stay short because they have little room.
* Keep placeholders like {{0}} or {{1}} exactly as they are; the program fills in a number or a name there.
* Please use the same words as the game itself where the game has a name for something (classes, maps, items, skills), because
  players know them from there. The English column and the German column help to see what is meant.
* Rows marked "check first" in the Notes column are new or changed.
* When you are done: save as CSV (UTF-8) with the same file name and send it back. Only the "Your text" column is read.

The file is plain text data. The program never runs anything from it and only takes over lines that pass a safety check
(known key, same placeholders, sensible length, no markup or links that the English text does not have).
"""


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "export")
    os.makedirs(out, exist_ok=True)
    master = load_master()
    for code, (english, native) in LANGUAGES.items():
        path = os.path.join(out, f"aiondps-translation-{code}.csv")
        with open(path, "w", encoding="utf-8-sig", newline="") as f:
            w = csv.writer(f, quoting=csv.QUOTE_ALL)
            w.writerow(COLUMNS)
            for key in sorted(master):
                v = master[key]
                en = v.get("en", "")
                cur = v.get(code, "")
                note = notes(en)
                if key in REVIEW_FIRST:
                    note = ("Check first (new or changed). " + note).strip()
                if not cur:
                    note = ("No text in this language yet: please translate. " + note).strip()
                w.writerow([key, where(key), safe_cell(en), safe_cell(v.get("de", "")), safe_cell(cur), safe_cell(cur), note])
        with open(os.path.join(out, f"README-{code}.txt"), "w", encoding="utf-8") as f:
            f.write(README.format(english=english, native=native, file=os.path.basename(path)))
        print(f"{code}: {len(master)} lines -> {path}")


if __name__ == "__main__":
    main()
