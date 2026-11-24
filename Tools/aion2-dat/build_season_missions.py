"""Builds the list of the weekly season missions (the points of the weekly reward of the season missions) for the week points page.

Usage:  python3 build_season_missions.py <decoded table dir> <l10n dir> <out.json>

- decoded table dir: .../AION2/Content/Data/Table of a decoded client (Achievement, AchievementStep, AchievementReward, AchievementGroup, NpcData,
  EventSchedule as JSON, see decode in the update workflow).
- l10n dir: the folder with l10n_<language>.json (en-US, de-DE, ...), see decrypt_all_l10n.py.

A weekly mission is an achievement of a Group_Season_G1_<L|D>_<tab> group with ResetCycleType Weekly. Each step has a target count (AchievementStep) and the points
the weekly reward gets for it (AchievementReward.RewardPoint); the game adds the points when the step's reward is collected. The "Event" and "Unlock"
groups give no points and are left out. The step's title and text key is AchievementString_Archieve_STR_<group>_<step id>_<achievement_title|obj_desc>.
"""
import json
import os
import sys
from collections import defaultdict

LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
TABS = ["Abyss", "Arena", "PartyChallenge", "Nightmare", "Thema"]  # the game's tabs in order: Abyss, Arena, Transcendence, Nightmare, Challenge


def rows(directory, name):
    return json.load(open(os.path.join(directory, name + ".json"), encoding="utf-8"))["Properties"]["Data"]


def main():
    table_dir, l10n_dir, out_path = sys.argv[1:4]
    l10n = {code: json.load(open(os.path.join(l10n_dir, f"l10n_{file}.json"), encoding="utf-8")) for code, file in LANGS.items()}

    achievements = rows(table_dir, "Achievement")
    steps = defaultdict(list)
    for s in rows(table_dir, "AchievementStep"):
        steps[s["AchievementId"]].append(s)
    points = defaultdict(dict)
    for r in rows(table_dir, "AchievementReward"):
        points[r["AchievementId"]][r["Step"]] = r["RewardPoint"]
    npc_by_name = {n["Name"]: n["ID"]["Value"] for n in rows(table_dir, "NpcData")}
    groups = {g["Name"]: g for g in rows(table_dir, "AchievementGroup")}

    def text(key):
        return {code: t[key] for code, t in l10n.items() if key in t}

    missions = []
    for a in achievements:
        group = a["GroupName"]
        if not group.startswith("Group_Season_G1_") or "Weekly" not in a["ResetCycleType"]:
            continue
        race, tab = group[len("Group_Season_G1_")], group[len("Group_Season_G1_L_"):]
        if tab not in TABS:
            continue
        achievement = a["AchievementId"]["Value"]
        ordered = sorted(steps[achievement], key=lambda s: s["Step"])
        if not ordered or not any(points[achievement].get(s["Step"]) for s in ordered):
            continue
        kind = a["ObjType"].split("::")[1]
        values = [v.strip() for joined in a["ObjValues"] for v in joined.split(",") if v.strip()]
        entry = {
            "id": achievement,
            "race": race,
            "tab": tab,
            "kind": kind,
            "steps": [
                {
                    "step": s["Step"],
                    "count": s["Count"],
                    "points": points[achievement].get(s["Step"], 0),
                    "title": text(f"AchievementString_Archieve_STR_{group}_{s['DataId']['Value']}_achievement_title"),
                    "text": text(f"AchievementString_Archieve_STR_{group}_{s['DataId']['Value']}_obj_desc"),
                }
                for s in ordered
            ],
        }
        if kind == "KillNpc":
            entry["npcs"] = sorted({npc_by_name[v] for v in values if v in npc_by_name})
        elif kind.startswith("AbyssArtifactEvent"):
            entry["event"] = values[0] if values else "abyss_ar1_artifactwar"
        missions.append(entry)

    events = {}
    for r in rows(table_dir, "EventSchedule"):
        for e in r["EventList"]:
            if e["Key"].endswith("_artifactwar"):
                days = [i for i, k in enumerate(["bSunday", "bMonday", "bTuesday", "bWednesday", "bThursday", "bFriday", "bSaturday"]) if r.get(k)]
                events[e["Key"]] = {"days": days, "at": [round(t["Ticks"] / 36e9, 2) for t in r["ActiveTimeList"]]}

    tab_names = {}
    for tab in TABS:
        key = f"String_{groups[f'Group_Season_G1_L_{tab}']['Title']['Key']}_body"
        tab_names[tab] = {c: t[key] for c, t in l10n.items() if key in t}

    json.dump({"tabs": TABS, "tabNames": tab_names, "events": events, "missions": missions}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("wrote", out_path, len(missions), "missions")


if __name__ == "__main__":
    main()
