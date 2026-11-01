"""Builds Backend/src/data/aion2/pantheon.json: the Pantheon decorations (AgitDecoItem) with their type, race, icon, grade, effects and names
in every client language, the 17 slots (AgitDecoSlot) and the names of the stats they raise.

Usage:  python3 -I build_pantheon.py <decoded Data/Table dir> <dir with l10n_<locale>.json> <out.json> [item_names.json]

The icon is the IconRes of the row (texture Item/ETC/<icon>, converted by Tools/aion2-icons/convert_pantheon_icons.py); the grade is the one of the decoration's item
(paintings 534310000 + id, statues 534320000 + id - 999, colossi 534330000 + id - 4999, bound or not) from item_names.json. Where the table has no item
(about half of the decorations, among them second ids of paintings that share name and icon with a first one) the grade is taken from the other decoration with the
same icon, else from the effect value, which the known decorations show to follow the grade exactly: paintings 2/3/4/5 = grade 2-5, statues 6/8/10/12/15 = grade 1-5,
colossi 35/45/55 = grade 2/4/5; 0 when none of that applies.

A decoration is the number the game sends in the Pantheon frames (0x8ce2 tail, 0xaf8a, 0x96e2): paintings 1.., statues 1000.., colossi 5000...
Its effects are the SkillAbnormalEffect rows of the decoration's AbnormalId ("Values": [stat, value, ...]); the window adds them up.
Names: l10n "String_<Desc key>_body"; stats: "String_StatName_<stat>_body" (e.g. "Tod [Triniel]").
"""
import json
import os
import sys

table_dir, l10n_dir, out_path = sys.argv[1:4]
item_names = json.load(open(sys.argv[4], encoding="utf-8")) if len(sys.argv) > 4 else {}
locales = {"de": "de-DE", "en": "en-US", "es": "es-ES", "fr": "fr-FR", "ja": "ja-JP", "ko": "ko-KR", "pt": "pt-BR", "ru": "ru-RU"}
texts = {code: json.load(open(os.path.join(l10n_dir, f"l10n_{loc}.json"), encoding="utf-8")) for code, loc in locales.items()}


def rows(name):
    return json.load(open(os.path.join(table_dir, name + ".json"), encoding="utf-8"))["Properties"]["Data"]


def names(key):
    got = {code: t.get(key) for code, t in texts.items()}
    return {k: v for k, v in got.items() if v and v != "???"}


effects = {}
for r in rows("SkillAbnormalEffect"):
    if r.get("AbnormalEffectType") != "EAbnormalEffectType::StatChange":
        continue
    stat, value = r["Values"][0][:1].upper() + r["Values"][0][1:], r["Values"][1]  # the table writes Illusion once as "illusion"
    effects.setdefault(r["AbnormalId"]["Value"], []).append([stat, int(value)])

def grade_of(deco_id):
    """The grade of the decoration's item (1 common .. 5), 0 when the item is not in the table."""
    base = 534310000 + deco_id if deco_id < 1000 else 534320000 + deco_id - 999 if deco_id < 5000 else 534330000 + deco_id - 4999
    for item in (base, base - 20000000):  # bound (534...) or not bound (514...)
        row = item_names.get(str(item))
        if row:
            return int(row[0])
    return 0


kinds = {"EDecoType::Frame": "frame", "EDecoType::Small": "statue", "EDecoType::Large": "colossus"}
decorations = {}
stats = set()
for r in rows("AgitDecoItem"):
    deco_id = r["ID"]["Value"]
    eff = effects.get(r["AbnormalId"]["Value"], [])
    stats.update(s for s, _ in eff)
    decorations[str(deco_id)] = {
        "k": kinds[r["EquipType"]],
        "r": r["Race"].split("::")[1],
        "i": r["IconRes"],
        "g": grade_of(deco_id),
        "n": names(f"String_{r['Desc']['Key']}_body"),
        "e": eff,
    }

# fill the grades the item table does not know: the same icon on a decoration that has one, then the effect value
by_icon = {d["i"].lower(): d["g"] for d in decorations.values() if d["g"]}
VALUE_GRADE = {"frame": {2: 2, 3: 3, 4: 4, 5: 5}, "statue": {6: 1, 8: 2, 10: 3, 12: 4, 15: 5}, "colossus": {35: 2, 45: 4, 55: 5}}
for d in decorations.values():
    if d["g"] == 0:
        values = {v for _, v in d["e"]}
        d["g"] = by_icon.get(d["i"].lower()) or (VALUE_GRADE[d["k"]].get(next(iter(values))) if len(values) == 1 else 0) or 0

slots = [{"slot": r["ID"]["Value"], "k": kinds[r["DecoType"]]} for r in rows("AgitDecoSlot")]
stat_names = {s: names(f"String_StatName_{s}_body") for s in sorted(stats)}
json.dump({"slots": slots, "stats": stat_names, "decorations": decorations}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"), sort_keys=True)
print(len(decorations), "decorations,", len(stats), "stats:", ", ".join(sorted(stats)))
