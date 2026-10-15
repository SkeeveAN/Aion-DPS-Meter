"""Shared helpers for the translation export/import tools (data only: nothing in a returned file is ever executed)."""
import csv
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
MASTER = os.environ.get("AIONDPS_MASTER") or os.path.normpath(os.path.join(HERE, "..", "..", "Client", "assets", "i18n", "ui_strings.json"))

# language code -> (English name, native name)
LANGUAGES = {
    "de": ("German", "Deutsch"),
    "fr": ("French", "Français"),
    "es": ("Spanish", "Español"),
    "ru": ("Russian", "Русский"),
    "zh": ("Chinese (simplified)", "中文"),
    "zh-TW": ("Chinese (traditional)", "繁體中文"),
    "pt": ("Portuguese (Brazil)", "Português (Brasil)"),
    "ja": ("Japanese", "日本語"),
    "ko": ("Korean", "한국어"),
}

COLUMNS = ["Key", "Where it appears", "English (source)", "Deutsch (reference)", "Current text", "Your text (edit here)", "Notes"]

PLACEHOLDER = re.compile(r"\{\d+\}")
DANGEROUS_START = "=+-@\t\r"

AREAS = {
    "Main": "Main window", "Settings": "Settings window", "Dummy": "Dummy test window", "Sessions": "Session manager / combat analyzer",
    "Details": "Player details", "ThemeEditor": "Design editor", "Character": "Character window", "Network": "Network analysis overlay",
    "Checklist": "Checklist window", "Rotation": "Skill rotation view", "Feedback": "Feedback window", "History": "Fight history",
    "Combat": "Boss fight overlay", "Wizard": "First-start wizard", "Assist": "Skill assist window", "Timetable": "Timetable overlay",
    "Sound": "Sound names", "PetFarm": "Pet info overlay", "Replay": "Fight replay", "Boss": "World boss overlay", "Upload": "Upload messages",
    "Overlay": "Overlay (general)", "PetShop": "Pet shop", "Stream": "Stream overlay (OBS)", "Npcap": "Capture driver (Npcap)",
    "PetMap": "Interactive map", "HotkeyOverlay": "Hotkey overlay",
}
KINDS = [
    (re.compile(r"\.(Tip|Tooltip)$|\.Tip\."), "tooltip (appears when the mouse rests on a control)"),
    (re.compile(r"Hint$|\.Hint\.|Note$"), "explanatory text (a sentence or paragraph)"),
    (re.compile(r"Title$"), "window or section title"),
    (re.compile(r"Button$|\.Save$|\.Cancel$|\.Close$|\.Start$|\.Stop$"), "button text (keep it short)"),
    (re.compile(r"^Main\.Menu|\.Menu"), "menu entry (keep it short)"),
    (re.compile(r"^Sound\."), "name of a sound"),
    (re.compile(r"^Main\.Mode"), "name of a display mode (button tooltip and column titles)"),
]


def load_master():
    with open(MASTER, encoding="utf-8") as f:
        return json.load(f)


def where(key):
    area = key.split(".")[0]
    text = AREAS.get(area, area)
    kind = next((label for pattern, label in KINDS if pattern.search(key)), "label")
    return f"{text}: {kind}"


def notes(en):
    out = []
    if PLACEHOLDER.search(en):
        out.append("Keep {0}, {1} ... exactly as they are: the program puts a number or a name there. You may move them within the sentence.")
    if "\n" in en:
        out.append("The line break is intentional.")
    if len(en) <= 14:
        out.append("Short text: it must fit in a small button or column.")
    return " ".join(out)


def safe_cell(text):
    """Spreadsheet programs run cells that start with = + - @ as formulas; a leading apostrophe makes them plain text again."""
    return "'" + text if text and text[0] in DANGEROUS_START else text


def unsafe_cell(text, original):
    """The reverse of safe_cell, but only where the apostrophe was added by us (the original text really starts with that character)."""
    if len(text) > 1 and text[0] == "'" and text[1] in DANGEROUS_START and original[:1] == text[1]:
        return text[1:]
    return text
