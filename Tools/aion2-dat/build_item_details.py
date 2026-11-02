"""Item details for the partner API (GET /api/partner/v1/items/<id>): Backend/src/data/aion2/item_details.json.

What the in-game tooltip shows, from the client's Item table (decoded in ~/aion2-data/decoded) and its texts (~/aion2-data/dec/l10n):
base stats (MainStats), the pool the soul binding rolls from (SubStats: min/max per stat, SoulbindRandomStatCount of them on the first binding,
SoulbindRandomSkillCountMax skills), required level (PermitLevelMin/Max), mana stone and god stone sockets, soul fuse (SoulAddGroup), enchant / surpass /
succession groups, trade type, sell price, the long description. Items are those of boss_loot.json (what the dungeon codex links to); add more ids with
extra ids on the command line.

Stat values are the client's own numbers. unit "percent" = hundredths of a percent (820 = 8.2 %), "flat" = as is. The tokens of the percentage stats are the
ones the website's gear tooltips already treat so (item_stats.json, matched against in-game tooltips); a stat the game shows as a rate is named *Rate in
English (DecreaseWeaponBlock = "Parry Damage Reduction Rate", 3300 = 33 %).

  python3 Tools/aion2-dat/build_item_details.py [extra item ids ...]
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.expanduser("~/aion2-data")
TABLES = os.path.join(DATA, "decoded", "AION2", "Content", "Data", "Table")
L10N = os.path.join(DATA, "dec", "l10n")
OUT = os.path.join(ROOT, "Backend", "src", "data", "aion2", "item_details.json")
LANGS = {"en": "en-US", "de": "de-DE", "fr": "fr-FR", "es": "es-ES", "ru": "ru-RU", "pt": "pt-BR", "ja": "ja-JP", "ko": "ko-KR"}

PERCENT = {
    "CombatSpeed", "DecreaseCriticalDamage", "AbnormalAccuracy", "AmplifyAllDamage", "AmplifyWeaponDamage", "AmplifyCriticalDamage", "AmplifyBackAttack",
    "DecreaseDamage", "DecreaseWeaponDamage", "AdditionalHitRate", "AdditionalHitResistRate", "AbnormalResistance", "MoveSpeed", "AmplifyHpHealGet",
    "DamageRatio", "DefenseRatio", "Perfect", "HardHit", "Restoration", "IronWall", "PerfectResist", "HardHitResist", "IgnoreRestoration", "IgnoreIronWall",
    "DecreaseWeaponBlock", "DecreaseShieldBlock",
}
HUNDREDTHS = {"FPMax"}  # sent in hundredths, shown as a whole number (27193 -> 272)

l10n = {lang: json.load(open(os.path.join(L10N, f"l10n_{code}.json"), encoding="utf-8")) for lang, code in LANGS.items()}


def text(*keys):
    """The text in every language under the first key the client has (String_<key>_body)."""
    for key in keys:
        names = {lang: l10n[lang].get(f"String_{key}_body") for lang in LANGS}
        names = {k: v for k, v in names.items() if v}
        if names:
            return names
    return {}


def table(name):
    return json.load(open(os.path.join(TABLES, name + ".json"), encoding="utf-8"))["Properties"]["Data"]


def enum(value):
    return str(value).split("::")[-1] if value not in (None, "None") else None


def key(ref):
    k = ref.get("Key") if isinstance(ref, dict) else None
    return None if k in (None, "None") else k


def unit(token):
    return "percent" if token in PERCENT else "hundredths" if token in HUNDREDTHS else "flat"


rows = {r["ID"]["Value"]: r for r in table("Item")}
ids = sorted({int(i) for i in json.load(open(os.path.join(ROOT, "Backend", "src", "data", "aion2", "boss_loot.json"), encoding="utf-8"))["items"]} | {int(a) for a in sys.argv[1:]})

stats = {}  # token -> names


def stat(token_ref):
    token = token_ref.split("::")[-1]
    if token not in stats:
        stats[token] = {"names": text(f"StatName_{token}"), "unit": unit(token)}
    return token


categories = {}  # EquipCategory -> names
items = {}
for item_id in ids:
    r = rows.get(item_id)
    if not r:
        continue
    kind = enum(r.get("ItemType"))
    entry = {
        "names": text(key(r["Desc"])) if key(r.get("Desc")) else {},
        "type": (kind or "").lower(),
        "grade": (enum(r.get("ItemGrade")) or "").lower(),
        "tier": enum(r.get("ItemTier")),
        "itemLevel": r.get("ItemLevel") or None,
        "race": (enum(r.get("ItemRace")) or "all").lower(),
        "requiredLevel": r.get("PermitLevelMin") or None,
        "trade": enum(r.get("ExchangeType")),
        "stack": r.get("MaxStackCount") or 1,
        "sellPrice": r.get("SellPrice") or None,
        "icon": r.get("IconRes") or None,
    }
    if key(r.get("DescLong")):
        entry["description"] = text(key(r["DescLong"]))
    category = enum(r.get("EquipCategory"))
    if category:
        entry["category"] = category.lower()
        categories.setdefault(category.lower(), text(f"STR_ITEM_TOOLTIP_{category}", f"STR_ITEM_TOOLTIP_{category.lower()}"))
    if kind == "Equip":
        entry["mainStats"] = [[stat(s["Key"]), s["Value"]] for s in r.get("MainStats") or [] if not s["Key"].split("::")[-1].startswith("Max") or "Block" not in s["Key"]]
        entry["maxBlock"] = [[stat(s["Key"]), s["Value"]] for s in r.get("MainStats") or [] if s["Key"].split("::")[-1].startswith("Max") and "Block" in s["Key"]] or None
        pool = [[stat(s["Key"]), s["Value"]["MinValue"], s["Value"]["MaxValue"]] for s in r.get("SubStats") or []]
        entry["soulBind"] = {
            "firstBindStats": r.get("SoulbindRandomStatCount") or 0,
            "firstBindSkills": r.get("SoulbindRandomSkillCountMax") or 0,
            "pool": pool,
        } if r.get("SoulbindRandomStat") else None
        entry["soulFuse"] = bool(enum(r.get("SoulAddGroup")))
        entry["manaStoneSlots"] = r.get("MagicStoneSlotCount") or 0
        entry["godStoneSlots"] = r.get("GodStoneSlotCount") or 0
        entry["enchantable"] = bool(r.get("CanEnchant"))
        entry["surpassable"] = bool(enum(r.get("SurpassGroup")))
        entry["succession"] = bool(enum(r.get("SuccessionGroup")))
        entry["set"] = r.get("SetNames") or None
    items[str(item_id)] = {k: v for k, v in entry.items() if v is not None}

labels = {
    "requiredLevel": text("STR_ITEM_SLOT_TOOLTIP_EQUIPLEVEL_TITLE"),  # equipment
    "useLevel": text("STR_ITEM_SLOT_TOOLTIP_uselevel_title"),  # everything else
    "itemLevel": text("STR_ITEM_SLOT_TOOLTIP_itemlevel_title"),
    "soulBindRate": text("UI_ITEM_TOOLTIP_SOUL_BIND_PERCENT"),
    "soulBindFirst": text("STR_ITEM_TOOLTIP_SOULBIND_RANDOM_STATS_GUIDE"),
    "soulBindPossible": text("STR_ITEM_TOOLTIP_SOULBIND_RANDOM_STATS_LIST"),
    "soulBindOnce": text("STR_ITEM_TOOLTIP_SOULBIND_ONCE_ACQUIRE_GUIDE"),
    "soulFuse": text("UI_ITEM_TOOLTIP_SOULADD"),
    "soulFuseAvailable": text("UI_ENCHANT_SOULADD_GUIDETEXT"),
    "manaStoneSlot": text("STR_ITEM_TOOLTIP_MagicStone_Slot_Empty"),
    "godStoneSlot": text("UI_ITEM_TOOLTIP_GODSTONE_SLOT_EMPTY"),
    "bindOnPickup": text("UI_ITEM_TOOLTIP_SOULBIND_ACQUIRE"),
    "tradable": text("UI_ITEM_TOOLTIP_MOUSEOVER_TEXT_TRADE"),
    "notTradable": text("UI_ITEM_TOOLTIP_MOUSEOVER_TEXT_TRADE_NOT"),
    "sellPrice": text("UI_ITEM_TOOLTIP_MOUSEOVER_TEXT_PRICE"),
    "lightOnly": text("UI_ITEM_TOOLTIP_MOUSEOVER_TEXT_LIGHT"),
    "darkOnly": text("UI_ITEM_TOOLTIP_MOUSEOVER_TEXT_DARK"),
}
grades = {g.lower(): text(f"UI_PROBINFO_GRADE_{g.upper()}") or text(f"Tooltip_Grade_{g}") for g in ["Common", "Rare", "Unique", "Legend", "Epic", "Mythic", "Special"]}
json.dump({"labels": {k: v for k, v in labels.items() if v}, "grades": grades, "categories": categories, "stats": stats, "items": items}, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
print(len(items), "items,", len(stats), "stats,", os.path.getsize(OUT), "bytes ->", OUT)
print("missing labels:", [k for k, v in labels.items() if not v])
print("categories without name:", [k for k, v in categories.items() if not v])
print("stats without name:", [k for k, v in stats.items() if not v["names"]])
