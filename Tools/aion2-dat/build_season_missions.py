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

import re
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
TABS = ["Abyss", "Arena", "PartyChallenge", "Nightmare", "Thema"]  # the game's tabs in order: Abyss, Arena, Transcendence, Nightmare, Challenge

# The client has no Chinese texts (eight languages only), so the Chinese titles and texts are ours: a title by its English name without the Roman numeral of the step,
# a text by its English wording with the numbers cut out ({0}, {1}, ... in the order they stand in the English text). Names of places and of the bosses stay as the game
# writes them in Latin letters (Kaira, Argo, Nahma, Altgard, Verteron, Daeva, Arcana).
ZH_TITLES = {
    "Hometown of My Heart": ("我心之乡", "我心之鄉"), "Bare Minimum Morality": ("最低限度的道德", "最低限度的道德"), "Purify Reshanta": ("净化 Reshanta", "淨化 Reshanta"),
    "Watcher Kaira": ("监视者 Kaira", "監視者 Kaira"), "Argo, the Spirit King": ("灵魂之王 Argo", "靈魂之王 Argo"), "Guardian Lord Nahma": ("守护之神 Nahma", "守護之神 Nahma"),
    "Land Grabs": ("领土争夺", "領土爭奪"), "Down to the Last One": ("直到最后一人", "直到最後一人"), "Dominator of Lower Abyss": ("下层深渊的支配者", "下層深淵的支配者"),
    "Steady Efforts": ("持之以恒", "持之以恆"), "Me, Beyond Myself": ("超越自我", "超越自我"), "Way of the Transcended": ("超越者之路", "超越者之路"),
    "Transforms Upon Death": ("死亡时变异", "死亡時變異"), "Transcendence Speed Run": ("超越速通", "超越速通"), "Time to Go Home": ("该回家了", "該回家了"),
    "Toward Greater Power": ("迈向更强大的力量", "邁向更強大的力量"), "Friends With a Story": ("有故事的朋友", "有故事的朋友"), "Collecting Dream Shards": ("收集梦之碎片", "收集夢之碎片"),
    "3 Minutes of Nightmare": ("三分钟噩梦", "三分鐘惡夢"), "Nightmare Buster": ("噩梦克星", "惡夢剋星"), "Eternal Rest": ("永恒安息", "永恆安息"),
    "Hatched Bird": ("破壳而出", "破殼而出"), "Shooting for the Stars": ("追逐星辰", "追逐星辰"), "Baptism of Fire": ("火的洗礼", "火的洗禮"),
    "Pride of Elysea": ("天族的骄傲", "天族的驕傲"), "Marching Beneath the Elyos Banner": ("在天族旗帜下前进", "在天族旗幟下前進"),
    "Daeva of Enduring Deeds": ("持久功绩的 Daeva", "持久功績的 Daeva"), "Daeva of Glorious Deeds": ("辉煌功绩的 Daeva", "輝煌功績的 Daeva"),
    "Daeva of Extraordinary Deeds": ("非凡功绩的 Daeva", "非凡功績的 Daeva"), "Daeva's Book": ("Daeva 之书", "Daeva 之書"),
    "Pride of Asmodae": ("魔族的骄傲", "魔族的驕傲"), "Marching Beneath the Asmodian Banner": ("在魔族旗帜下前进", "在魔族旗幟下前進"),
}
ZH_TEXTS = {
    "Spend N hour in Abyss": ("在深渊停留{0}小时", "在深淵停留{0}小時"),
    "Kill N opposing camp players": ("击败{0}名敌对阵营玩家", "擊敗{0}名敵對陣營玩家"),
    "Hunt N monsters in Abyss": ("在深渊狩猎{0}只怪物", "在深淵狩獵{0}隻怪物"),
    "Defeat the boss Watcher Kaira N time": ("击败首领监视者 Kaira {0}次", "擊敗首領監視者 Kaira {0}次"),
    "Defeat the boss Argo, the Spirit King N time": ("击败首领灵魂之王 Argo {0}次", "擊敗首領靈魂之王 Argo {0}次"),
    "Defeat the boss Guardian Lord Nahma N time": ("击败首领守护之神 Nahma {0}次", "擊敗首領守護之神 Nahma {0}次"),
    "Participate in Artifact Siege N time by its end": ("参加神器攻城战直至结束{0}次", "參加神器攻城戰直至結束{0}次"),
    "Win Artifact Siege N time": ("赢得神器攻城战{0}次", "贏得神器攻城戰{0}次"),
    "Defeat N Dominator of Lower Abyss after Artifact Siege": ("神器攻城战后击败{0}名下层深渊的支配者", "神器攻城戰後擊敗{0}名下層深淵的支配者"),
    "Participate in Arena N time": ("参加竞技场{0}次", "參加競技場{0}次"),
    "Win N time in Arena": ("在竞技场获胜{0}次", "在競技場獲勝{0}次"),
    "Clear Transcendence Dungeon N time": ("通关超越地下城{0}次", "通關超越地城{0}次"),
    "Clear Transcendence Dungeon N time without dying": ("不死亡通关超越地下城{0}次", "不死亡通關超越地城{0}次"),
    "Clear Transcendence Dungeon within N minute": ("在{0}分钟内通关超越地下城", "在{0}分鐘內通關超越地城"),
    "Defeat N Mini-Bosses in Transcendence Dungeon": ("在超越地下城击败{0}个中型首领", "在超越地城擊敗{0}個中型首領"),
    "Extract N Arcana": ("提取{0}个 Arcana", "提取{0}個 Arcana"),
    "Transmute N Arcana": ("转化{0}个 Arcana", "轉化{0}個 Arcana"),
    "Defeat Nightmare Boss N time": ("击败噩梦首领{0}次", "擊敗惡夢首領{0}次"),
    "Obtain N Phantasmal Fragments": ("获得{0}个幻影碎片", "獲得{0}個幻影碎片"),
    "Defeat Nightmare Boss within N minute": ("在{0}分钟内击败噩梦首领", "在{0}分鐘內擊敗惡夢首領"),
    "Clear Ascension Trial N time": ("通关飞升试炼{0}次", "通關飛升試煉{0}次"),
    "Kill [N-N Star Officer] N time": ("击杀[{0}至{1}星军官]{2}次", "擊殺[{0}至{1}星軍官]{2}次"),
    "Kill [First-Class to Ninth-Class Soldier] N time": ("击杀[一等至九等士兵]{0}次", "擊殺[一等至九等士兵]{0}次"),
    "Kill N Asmodian players in Altgard": ("在 Altgard 击败{0}名魔族玩家", "在 Altgard 擊敗{0}名魔族玩家"),
    "Kill N Asmodian players in Verteron": ("在 Verteron 击败{0}名魔族玩家", "在 Verteron 擊敗{0}名魔族玩家"),
    "Kill N Elyos players in Verteron": ("在 Verteron 击败{0}名天族玩家", "在 Verteron 擊敗{0}名天族玩家"),
    "Kill N Elyos players in Altgard": ("在 Altgard 击败{0}名天族玩家", "在 Altgard 擊敗{0}名天族玩家"),
    "Gather N Odyle in Abyss": ("在深渊采集{0}个奥迪尔", "在深淵採集{0}個奧迪爾"),
    "Complete N Abyss commands": ("完成{0}个深渊指令", "完成{0}個深淵指令"),
    "Defeat N Abyss boss monster": ("击败{0}只深渊首领怪物", "擊敗{0}隻深淵首領怪物"),
    "Participate in Battlefield N time": ("参加战场{0}次", "參加戰場{0}次"),
}
ZH_TABS = {"Abyss": ("深渊", "深淵"), "Arena": ("竞技场", "競技場"), "PartyChallenge": ("超越", "超越"), "Nightmare": ("噩梦", "惡夢"), "Thema": ("挑战", "挑戰")}
ROMAN = re.compile(r"\s*([ⅠⅡⅢⅣⅤⅥⅦⅧⅨⅩ]+|[IVX]+)$")
NUMBER = re.compile(r"\d[\d,.]*")


def chinese(title_en, text_en):
    """({zh, zh-TW} title, {zh, zh-TW} text) of a step; fails when a title or wording has no translation (a new mission of a new season needs one)."""
    m = ROMAN.search(title_en)
    base, numeral = (title_en[:m.start()].strip(), m.group(1)) if m else (title_en, "")
    pattern = re.sub(r"\s+", " ", NUMBER.sub("N", text_en)).strip()
    # singular and plural share one key: "N times"/"N time", "N minutes"/"N minute", "N hours", "boss monsters"
    pattern = pattern.replace(" times", " time").replace(" minutes", " minute").replace(" hours", " hour").replace("boss monsters", "boss monster")
    numbers = NUMBER.findall(text_en)
    title = {c: (ZH_TITLES[base][i] + (" " + numeral if numeral else "")) for i, c in enumerate(("zh", "zh-TW"))}
    text = {c: ZH_TEXTS[pattern][i].format(*numbers) for i, c in enumerate(("zh", "zh-TW"))}
    return title, text


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

    def step_entry(s, group, achievement):
        title = text(f"AchievementString_Archieve_STR_{group}_{s['DataId']['Value']}_achievement_title")
        desc = text(f"AchievementString_Archieve_STR_{group}_{s['DataId']['Value']}_obj_desc")
        zh_title, zh_text = chinese(title["en"], desc["en"])
        return {"step": s["Step"], "count": s["Count"], "points": points[achievement].get(s["Step"], 0), "title": {**title, **zh_title}, "text": {**desc, **zh_text}}

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
                step_entry(s, group, achievement)
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
        tab_names[tab]["zh"], tab_names[tab]["zh-TW"] = ZH_TABS[tab]

    json.dump({"tabs": TABS, "tabNames": tab_names, "events": events, "missions": missions}, open(out_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("wrote", out_path, len(missions), "missions")


if __name__ == "__main__":
    main()
