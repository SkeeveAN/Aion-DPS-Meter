"""Chain skills of the client: Skill.json rows with ChainSkillPrevSkillId. Writes Backend/src/data/aion2/skill_chains.json as
{ "<prev skill id>": [[<next skill id>, <window ms>], ...] }. The window is ChainSkillAvailableTime (3000 ms for nearly all).
Usage: python -I build_skill_chains.py <decoded Skill.json> <out.json>"""
import json
import sys

rows = json.load(open(sys.argv[1]))["Properties"]["Data"]
chains = {}
for r in rows:
    prev = r.get("ChainSkillPrevSkillId", {}).get("Value", 0)
    if prev:
        chains.setdefault(str(prev), []).append([r["ID"]["Value"], r.get("ChainSkillAvailableTime", 3000)])
for v in chains.values():
    v.sort()
json.dump(dict(sorted(chains.items(), key=lambda kv: int(kv[0]))), open(sys.argv[2], "w"), separators=(",", ":"))
print(len(chains), "skills with a follow-up,", sum(len(v) for v in chains.values()), "chain links")
