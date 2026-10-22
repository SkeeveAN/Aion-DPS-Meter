# Aion 2 boss NPC ids

`boss_npcs.json` maps the NPC id the game sends in the "monster appears" frame to the boss's English
name and instance. Generated from `Backend/src/data/aion2/bosses.json` + `instances.json` (derived
facts: names and NPC ids, no prose):

```
python3 - <<'PY'   # from the repository root
import json
bs=json.load(open('Backend/src/data/aion2/bosses.json'))['items']
ins={i['key']:i['name']['en'] for i in json.load(open('Backend/src/data/aion2/instances.json'))['items']}
out={str(n):{'name':b['name']['en'],'instance':ins.get(b['instanceKey'],b['instanceKey'])} for b in bs for n in b.get('npcIds',[])}
# write {"_readme": [...], "bosses": out} to Client/assets/aion2/npcs/boss_npcs.json
PY
```

The generated entries carry no hit points. `maxHp` (and `hpScale: "party"` for the exploration bosses whose hit points
grow with the group size, 1 to 5 players) is merged in afterwards from `Backend/src/content/bossHpSeed.json`
(the full hit points of the boss, or those for ONE player when `hpScale` is `party`); regenerating the file must keep
those two fields.

The mapping was verified on a recorded Krao Cave run (2300104 = Enhanced Harcon, 2300171 = Ultimate Berk).
