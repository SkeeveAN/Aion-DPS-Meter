"""Builds Backend/src/data/aion2/pantheon.json: the Pantheon decorations (AgitDecoItem) with their type, race, effects and names
in every client language, the 17 slots (AgitDecoSlot) and the names of the stats they raise.

Usage:  python3 -I build_pantheon.py <decoded Data/Table dir> <dir with l10n_<locale>.json> <out.json>

A decoration is the number the game sends in the Pantheon frames (0x8ce2 tail, 0xaf8a, 0x96e2): paintings 1.., statues 1000.., colossi 5000...
Its effects are the SkillAbnormalEffect rows of the decoration's AbnormalId ("Values": [stat, value, ...]); the window adds them up.
Names: l10n "String_<Desc key>_body"; stats: "String_StatName_<stat>_body" (e.g. "Tod [Triniel]").
"""
import json
import os
import sys

table_dir, l10n_dir, out_path = sys.argv[1:4]
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
        "n": names(f"String_{r['Desc']['Key']}_body"),
        "e": eff,
    }

slots = [{"slot": r["ID"]["Value"], "k": kinds[r["DecoType"]]} for r in rows("AgitDecoSlot")]
stat_names = {s: names(f"String_StatName_{s}_body") for s in sorted(stats)}
json.dump({"slots": slots, "stats": stat_names, "decorations": decorations}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"), sort_keys=True)
print(len(decorations), "decorations,", len(stats), "stats:", ", ".join(sorted(stats)))
