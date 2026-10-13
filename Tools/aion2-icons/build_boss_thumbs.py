"""Boss pictures for the client (WPF cannot read .webp): small PNG thumbnails of the website's boss cards, and an index
npc id -> file, written to Client/assets/aion2/bosses/thumbs/.

The website names its cards in Web-Frontend/game-data.js ("Afflicted Bakarma": "/images/aion2/bosses/cards/afflicted-bakarma.webp");
the NPC ids of each boss are in Backend/src/data/aion2/bosses.json (by English name). A boss without a card gets no entry:
the combat overlay draws a plain emblem for it.

Run from the repository root:  python3 Tools/aion2-icons/build_boss_thumbs.py
"""
import json
import os
import re

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Client", "assets", "aion2", "bosses", "thumbs")
CARDS = os.path.join(ROOT, "Web-Frontend", "images", "aion2", "bosses", "cards")
THUMB = (128, 72)

game_data = open(os.path.join(ROOT, "Web-Frontend", "game-data.js"), encoding="utf-8").read()
cards = {m.group(1): m.group(2) for m in re.finditer(r'"([^"]+)":\s*"/images/aion2/bosses/cards/([^"]+)\.webp"', game_data)}
bosses = json.load(open(os.path.join(ROOT, "Backend", "src", "data", "aion2", "bosses.json"), encoding="utf-8"))["items"]

os.makedirs(OUT, exist_ok=True)
index = {}
written = set()
for boss in bosses:
    card = cards.get(boss["name"]["en"])
    if not card:
        continue
    source = os.path.join(CARDS, card + ".webp")
    if not os.path.exists(source):
        continue
    if card not in written:
        image = Image.open(source).convert("RGB")
        image.thumbnail(THUMB, Image.LANCZOS)
        image.save(os.path.join(OUT, card + ".png"), optimize=True)
        written.add(card)
    for npc in boss.get("npcIds", []):
        index[str(npc)] = card + ".png"

with open(os.path.join(OUT, "index.json"), "w", encoding="utf-8") as f:
    json.dump({"_readme": "npc id -> thumbnail; built by Tools/aion2-icons/build_boss_thumbs.py", "thumbs": index}, f, indent=0)
print(f"{len(written)} thumbnails, {len(index)} npc ids")
