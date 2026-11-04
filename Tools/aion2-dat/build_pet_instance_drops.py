"""Pet soul drops of instance and Abyss monsters for the partner API: Backend/src/data/aion2/pet_instance_drops.json.

Since the patch of 2026-11-04 the souls of certain pets also drop from monsters of Expedition and Transcendence dungeons (before, those monsters had an
empty reward list; only bosses gave a soul). The facts are in the client's RewardItem table: a reward row named `reward_PartyDungeon*_<npc id>` (instances),
`reward_Abyss_*_<npc id>` (Abyss) or `reward_Raid_*` that lists a pet soul item (the chance is always 100 in the table; the server decides the real drop).
The pet is the soul item's pet (Client/assets/aion2/pets/pets.json "souls"). The instance comes from the monster's internal name (M_PD_<dungeon>_...); an
expedition is told apart by the NPC id's step: 231... exploration, 230... conquest, 232... conquest hard (see the Conquest [Hard] notes).

  AION2_TABLES=... python3 Tools/aion2-dat/build_pet_instance_drops.py
"""
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DATA = os.path.expanduser("~/aion2-data")
TABLES = os.environ.get("AION2_TABLES") or os.path.join(DATA, "decoded", "AION2", "Content", "Data", "Table")
OUT = os.path.join(ROOT, "Backend", "src", "data", "aion2", "pet_instance_drops.json")
PREFIX = {"Krao": "Krao", "UC": "UC", "Fire": "FT", "Draupnir": "Draupnir", "BakronIsland": "Bakron", "FierceHornCave": "MauCave", "DDN": "DDN", "CradleNihility": "CradleNihility",
          "BlueBreathIsle": "BlueBreathIsle", "Arkanis": "Arkanis", "SunkenTemple": "SunkenTemple", "MirrorHeart": "MirrorHeart", "DeusCenter": "DeusCenter",
          "ErosionPurifier": "ErosionPurifier", "MusphelHolyGrail": "MusphelHolyGrail", "ReforgedAbyss": "ReforgedAbyss"}


def table(name):
    return json.load(open(os.path.join(TABLES, name + ".json"), encoding="utf-8"))["Properties"]["Data"]


pets = json.load(open(os.path.join(ROOT, "Client", "assets", "aion2", "pets", "pets.json"), encoding="utf-8"))
instance_keys = {i["key"] for i in json.load(open(os.path.join(ROOT, "Backend", "src", "data", "aion2", "instances.json"), encoding="utf-8"))["items"]}
items = {r["Name"]: r["ID"]["Value"] for r in table("Item")}
soul_pet = {name: pets["souls"][str(i)] for name, i in items.items() if str(i) in pets["souls"]}
npc = {r["ID"]["Value"]: r["Name"] for r in table("NpcData")}
out, seen = [], set()
for row in table("RewardItem"):
    m = re.match(r"reward_(PartyDungeon|PartyDungeonBoss|Abyss|Raid)_", row["Name"])
    souls = [x["ItemName"] for x in row["Items"] if x["ItemName"] in soul_pet]
    if not m or not souls:
        continue
    ids = re.findall(r"_(2\d{6})(?:_|$)", row["Name"])
    if not ids:
        continue
    npc_id = int(ids[0])
    internal = npc.get(npc_id, "")
    scope = "abyss" if m.group(1) == "Abyss" else "raid" if m.group(1) == "Raid" else "instance"
    key, mode = None, None
    d = re.match(r"M_PD_([A-Za-z]+)_", internal)
    if d and d.group(1) in PREFIX:
        base = PREFIX[d.group(1)]
        step = str(npc_id)[:3]
        if base in ("Krao", "UC", "FT", "Draupnir", "Bakron", "MauCave"):
            mode, key = {"231": ("explore", base), "230": ("conquest", base + ":conquest"), "232": ("advanced", base + ":hard")}.get(step, (None, base))
        else:
            key = base
        if key not in instance_keys:
            key = base if base in instance_keys else None
    for soul in souls:
        sig = (npc_id, soul_pet[soul], key, mode)
        if sig in seen:
            continue
        seen.add(sig)
        out.append({"npcId": npc_id, "petId": soul_pet[soul], "soulItemId": items[soul], "scope": scope, "instanceKey": key, "mode": mode})
out.sort(key=lambda x: (x["scope"], x["instanceKey"] or "", x["npcId"], x["petId"]))
json.dump({"_meta": {"source": "client table RewardItem, NpcData (EU client, patch of 2026-11-04)", "note": "Monsters of instances and the Abyss whose reward lists a pet soul; since the patch of 2026-11-04 (before: bosses only). The chance in the table is 100, the real drop is decided by the server.", "mode": "explore | conquest | advanced for the expeditions with these steps, else null"}, "drops": out}, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(len(out), "drops;", len({x["petId"] for x in out}), "pets;", sum(1 for x in out if x["instanceKey"] is None and x["scope"] == "instance"), "instance rows without an instance")
