"""Boss portraits and loot for the partner API: Backend/src/data/aion2/boss_loot.json and Web-Frontend/images/aion2/bosses/portraits/<file>.webp.

Sources (decoded client tables in ~/aion2-data/decoded, texts in ~/aion2-data/dec/l10n, dumped portrait packages):
  NpcData.Portrait            -> portrait name (UI_OEC_MOB_Cromede_01 = texture UT_256_MOB_Cromede_01, 256 px DXT5)
  RewardOdEnergyCube          -> per dungeon and mode: final boss + mid bosses (NPC names) with their reward names, Odyle cost, guaranteed items
  RewardPackage               -> the rolls of a reward ("Zufaelliger Erhalt 1..n": chance, number of times, package)
  RewardPackageItem           -> the items of a package with their single chances
Chances are in millionths of a percent (7500000 = 7.5 %, 100000000 = 100 %); a value above 100 % means that many sure rolls (300 % = 3 times).
Several dungeons not released yet carry a copy of the Draupnir cube in the table (Draupnir boss names, Draupnir reward); those are left out.

  python3 Tools/aion2-dat/build_boss_loot.py <dir with the dumped UT_256_MOB_*.uasset>
"""
import io
import json
import os
import re
import struct
import sys
from collections import Counter

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.expanduser("~/aion2-data")
TABLES = os.path.join(DATA, "decoded", "AION2", "Content", "Data", "Table")
L10N = os.path.join(DATA, "dec", "l10n")
RAW = sys.argv[1]
OUT_JSON = os.path.join(ROOT, "Backend", "src", "data", "aion2", "boss_loot.json")
OUT_IMG = os.path.join(ROOT, "Web-Frontend", "images", "aion2", "bosses", "portraits")
ICON_DIR = os.path.join(ROOT, "Web-Frontend", "images", "aion2", "icons", "item")
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}


def table(name):
    return json.load(open(os.path.join(TABLES, name + ".json"), encoding="utf-8"))["Properties"]["Data"]


l10n = {lang: json.load(open(os.path.join(L10N, f"l10n_{code}.json"), encoding="utf-8")) for lang, code in LANGS.items()}


def text(key):
    names = {lang: l10n[lang].get(f"String_{key}_body") for lang in LANGS}
    return {k: v for k, v in names.items() if v}


npc_by_id = {n["ID"]["Value"]: n for n in table("NpcData")}
npc_ids_of_name = {}
for n in npc_by_id.values():
    npc_ids_of_name.setdefault(n["Name"], []).append(n["ID"]["Value"])
items = {i["Name"]: i for i in table("Item")}
packages = {p["Name"].lower(): p for p in table("RewardPackage")}
groups = {p["PackageName"].lower(): p for p in table("RewardPackageItem")}
item_icons = json.load(open(os.path.join(ROOT, "Backend", "src", "data", "aion2", "item_icons.json"), encoding="utf-8"))
grade_names = {g.lower(): text(f"UI_PROBINFO_GRADE_{g.upper()}") or text(f"Tooltip_Grade_{g}") for g in ["Common", "Rare", "Unique", "Legend", "Epic", "Mythic", "Special"]}


item_defs = {}


def item_entry(name, prob, lo, hi):
    """A line of a roll; what an item is (grade, icon, names) is kept once in item_defs."""
    row = items.get(name)
    if row is None:
        return None
    iid = row["ID"]["Value"]
    if iid not in item_defs:
        icon = item_icons.get(str(iid))
        item_defs[iid] = {"level": row["ItemLevel"] if row["ItemType"].endswith("Equip") else None, "grade": row["ItemGrade"].split("::")[-1].lower(), "icon": icon if icon and os.path.exists(os.path.join(ICON_DIR, icon + ".webp")) else None, "names": text(row["Desc"]["Key"])}
    return {"itemId": iid, "grade": item_defs[iid]["grade"], "chance": round(prob / 1e6, 6), "count": [lo, hi]}


def roll(entry):
    """One 'random obtain' line: the package behind it, as a group of items with a common grade."""
    prob = entry["Prob"]
    rolls, rest = divmod(prob, 100_000_000)
    times = rolls if rolls else 1
    chance = 100.0 if rolls else round(prob / 1e6, 6)
    group = groups.get(entry["Item"].lower())
    if group is not None:
        rows = [e for e in (item_entry(i["ItemName"], i["ItemProb"], i["ItemCountMin"], i["ItemCountMax"]) for i in group["Items"]) if e]
    else:
        single = item_entry(entry["Item"], 100_000_000, entry["Count"], entry["Count"])
        rows = [single] if single else []
    if not rows:
        return None
    grade = Counter(r["grade"] for r in rows).most_common(1)[0][0]
    return {"chance": chance, "times": times, "grade": grade, "items": rows}


def reward(name):
    package = packages.get(name.lower())
    if package is None:
        return []
    return [r for r in (roll(e) for e in package["PackageItems"]) if r]


def mode_of(cube):
    tail = cube["Name"].split("_", 2)[2] if cube["Name"].count("_") >= 2 else ""
    m = re.fullmatch(r"G_(\d+)", tail)
    if m:
        return f"grade{int(m.group(1))}"
    # the game's own three steps of an expedition: Exploration (NPC 231...), Conquest (230...), Conquest hard (232...)
    return {"easy": "explore", "normal": "conquest", "hard": "conquest-hard"}.get(tail.lower(), tail.lower())


# --- cubes: only the ones that belong to their own dungeon (placeholder copies point at the Draupnir reward)
dungeon_of = lambda c: c["Name"].split("_")[1]
by_npc_name = {}
for cube in table("RewardOdEnergyCube"):
    if dungeon_of(cube).upper() not in cube["RewardCommon"].upper():
        continue
    mode = mode_of(cube)
    base = {
        "cube": cube["Name"],
        "dungeon": dungeon_of(cube),
        "mode": mode,
        "odyleCost": cube["ConsumeTicketCount"],
        "ceiling": {"after": cube["CeilingMaxCount"], "rewards": cube["CeilingRewardCount"], "items": [e for e in (item_entry(i, 0, 1, 1) for i in cube["CeilingItems"]) if e]} if cube["CeilingItems"] else None,
    }
    by_npc_name[cube["FinalBossName"]] = {**base, "role": "final", "reward": cube["RewardCommon"], "portrait": cube["FinalBossPortrait"]}
    for mid in cube["RewardMiddleBossList"]:
        by_npc_name[mid["BossName"]] = {**base, "role": "mid", "reward": mid["RewardName"], "portrait": mid["Portrait"]}

# --- portraits
def dds(w, h, data):
    header = struct.pack("<4sIIIIIII44sII4sIIIII", b"DDS ", 124, 0x81007, h, w, len(data), 0, 1, b"\0" * 44, 32, 4, b"DXT5", 0, 0, 0, 0, 0)
    return header + struct.pack("<IIIII", 0x1000, 0, 0, 0, 0) + data


os.makedirs(OUT_IMG, exist_ok=True)
made = {}


def portrait_file(token):
    """UI_OEC_MOB_Cromede_01 / UI_PDC_MOB_Cromede_01_V02 -> 'cromede-01' file name, written once; None if the game has no picture."""
    m = re.fullmatch(r"UI_[A-Z]+_(MOB_.+)", token or "")
    if not m:
        return None
    stem = "UT_256_" + m.group(1)
    if stem in made:
        return made[stem]
    path = os.path.join(RAW, stem + ".uasset")
    made[stem] = None
    if not os.path.exists(path):
        return None
    d = open(path, "rb").read()
    i = d.rfind(b"PF_")
    if d[i:d.find(b"\0", i)] != b"PF_DXT5":
        return None
    w, h, _ = struct.unpack("<iii", d[i - 16:i - 4])
    n = w * h
    name = m.group(1)[4:].lower().replace("_", "-")
    Image.open(io.BytesIO(dds(w, h, d[len(d) - n - 12:len(d) - 12]))).convert("RGBA").save(os.path.join(OUT_IMG, name + ".webp"), "WEBP", quality=85, method=6)
    made[stem] = name
    return name


bosses = json.load(open(os.path.join(ROOT, "Backend", "src", "data", "aion2", "bosses.json"), encoding="utf-8"))["items"]
out = {}
with_loot = with_portrait = 0
for boss in bosses:
    portrait = None
    loot = []
    seen = set()
    for npc_id in boss["npcIds"]:
        npc = npc_by_id.get(npc_id)
        if npc is None:
            continue
        portrait = portrait or portrait_file(npc.get("Portrait"))
        hit = by_npc_name.get(npc["Name"])
        if hit is None or (hit["cube"], hit["role"]) in seen:
            continue
        seen.add((hit["cube"], hit["role"]))
        rolls = reward(hit["reward"])
        if rolls:
            loot.append({"dungeon": hit["dungeon"], "mode": hit["mode"], "role": hit["role"], "npcId": npc_id, "odyleCost": hit["odyleCost"], "rolls": rolls, "ceiling": hit["ceiling"]})
            portrait = portrait or portrait_file(hit["portrait"])
    if portrait or loot:
        out[boss["key"]] = {"portrait": portrait, "loot": loot}
        with_portrait += bool(portrait)
        with_loot += bool(loot)

json.dump({"_meta": {"source": "client tables (RewardOdEnergyCube, RewardPackage, RewardPackageItem, NpcData.Portrait), EU client 2.0.7", "chance": "percent; count = [min, max]; times = how often the roll is made (chance 100)"}, "modes": {"explore": text("UI_PARTYDUNGEON_DIFFICULTY_EXPLORER"), "conquest": text("UI_PARTYDUNGEON_DIFFICULTY_CONQUER"), "conquest-hard": {k: f"{text('UI_PARTYDUNGEON_DIFFICULTY_CONQUER').get(k, '')} - {v}" for k, v in text("UI_PARTYDUNGEON_CONQUER_DIFFICULTY_ADVANCED").items()}}, "grades": grade_names, "items": {str(k): v for k, v in sorted(item_defs.items())}, "bosses": out}, open(OUT_JSON, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
print(f"bosses {len(bosses)}, with portrait {with_portrait}, with loot {with_loot}, portraits written {sum(1 for v in made.values() if v)}")
