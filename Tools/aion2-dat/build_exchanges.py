"""Item exchanges (Substance Morph / "Wandlung") of the Abyssal Forge: Ludra for the partner API: Backend/src/data/aion2/exchanges.json.

Source: the client's ConversionRecipe table (decoded, ~/aion2-data/decoded*/...) and the item texts (l10n). Only recipes that make or take the Ludra gear change
vouchers (G_Material_EquipChange_A_u_01a_b = Normal, _02a_b = Hard) or the Ludra weapon choice box are kept. The Elyos and Asmodian recipes are identical, so one is kept.
GoldCombined is Kina. Kina costs are given as `kina`, the other components as {itemId, count}.

  AION2_TABLES=... AION2_L10N=... python3 Tools/aion2-dat/build_exchanges.py
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.expanduser("~/aion2-data")
TABLES = os.environ.get("AION2_TABLES") or os.path.join(DATA, "decoded", "AION2", "Content", "Data", "Table")
L10N = os.environ.get("AION2_L10N") or os.path.join(DATA, "dec", "l10n")
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}
WANTED = {"G_Material_EquipChange_A_u_01a_b", "G_Material_EquipChange_A_u_02a_b", "Box_Select_Raid_ReforgedAbyss_Epic_Weapon_A_01_b"}


def table(name):
    return json.load(open(os.path.join(TABLES, name + ".json"), encoding="utf-8"))["Properties"]["Data"]


l10n = {k: json.load(open(os.path.join(L10N, f"l10n_{v}.json"), encoding="utf-8")) for k, v in LANGS.items()}
items = {i["Name"]: i for i in table("Item")}


def names(item):
    key = items[item]["Desc"]["Key"]
    return {k: t[f"String_{key}_body"] for k, t in l10n.items() if t.get(f"String_{key}_body")}


def ref(item):
    return {"itemId": items[item]["ID"]["Value"], "names": names(item)}


out, seen = [], set()
for r in table("ConversionRecipe"):
    parts = [c["Item"] for c in r["ComponentList"]]
    if r["Product"] not in WANTED and not any(p in WANTED for p in parts):
        continue
    kina = sum(c["Count"] for c in r["ComponentList"] if c["Item"] == "GoldCombined")
    cost = [{**ref(c["Item"]), "count": c["Count"]} for c in r["ComponentList"] if c["Item"] != "GoldCombined"]
    sig = (r["Product"], r["ProductCount"], json.dumps(cost, sort_keys=True), kina)
    if sig in seen:
        continue
    seen.add(sig)
    out.append({"product": {**ref(r["Product"]), "count": r["ProductCount"]}, "cost": cost, "kina": kina})
choice = []
for box in table("BoxReward"):
    if box["Group"] == "Box_Select_Raid_ReforgedAbyss_Epic_Weapon_A_01_b":
        choice = [ref(s["Item"]) for s in box["SelectItems"]]
json.dump({"_meta": {"source": "client tables (ConversionRecipe, BoxReward, Item), EU client, patch of 2026-11-04", "note": "Exchanges of the Ludra gear change vouchers; kina is the Kina cost; weaponChoice lists what the Ludra weapon box offers"}, "exchanges": out, "weaponChoice": choice}, open(os.path.join(ROOT, "Backend", "src", "data", "aion2", "exchanges.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(len(out), "exchanges,", len(choice), "weapons")
